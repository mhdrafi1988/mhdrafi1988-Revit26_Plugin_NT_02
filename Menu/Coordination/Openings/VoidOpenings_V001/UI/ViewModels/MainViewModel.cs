using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels
{
    /// <summary>View model of the Void Openings window: the two model panels, the notification strip and the status line.</summary>
    public sealed class MainViewModel : ObservableObject
    {
        private readonly ModelSelectionService _models;
        private string _notification = string.Empty;
        private bool _notificationIsError;
        private string _statusLine = "Reading the project…";
        private bool _isBusy;

        /// <summary>Creates the view model.</summary>
        public MainViewModel(ModelSelectionService models)
        {
            _models = models ?? throw new ArgumentNullException(nameof(models));
            Structural = new ModelPanelViewModel("STRUCTURAL MODEL", "Structural link", _models.GetStructuralCountsAsync, ShowError);
            Mep = new ModelPanelViewModel("MEP MODEL", "MEP link", _models.GetMepCountsAsync, ShowError);
            RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
            DismissNotificationCommand = new RelayCommand(() => Notification = string.Empty);
        }

        /// <summary>Window title, with the tool version from <see cref="ToolCatalog"/>.</summary>
        public string Title => ToolCatalog.VoidOpenings.Title;

        /// <summary>Left panel.</summary>
        public ModelPanelViewModel Structural { get; }

        /// <summary>Right panel.</summary>
        public ModelPanelViewModel Mep { get; }

        /// <summary>Reads the links of the active project again.</summary>
        public IAsyncRelayCommand RefreshCommand { get; }

        /// <summary>Hides the notification strip.</summary>
        public IRelayCommand DismissNotificationCommand { get; }

        /// <summary>Message in the notification strip; empty hides the strip.</summary>
        public string Notification
        {
            get => _notification;
            private set
            {
                if (SetProperty(ref _notification, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasNotification));
            }
        }

        /// <summary>True when the strip shows a message.</summary>
        public bool HasNotification => _notification.Length > 0;

        /// <summary>True when the message is an error, false for information.</summary>
        public bool NotificationIsError
        {
            get => _notificationIsError;
            private set => SetProperty(ref _notificationIsError, value);
        }

        /// <summary>Text of the status bar.</summary>
        public string StatusLine
        {
            get => _statusLine;
            private set => SetProperty(ref _statusLine, value);
        }

        /// <summary>True while the model list is being read.</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                    RefreshCommand.NotifyCanExecuteChanged();
            }
        }

        /// <summary>First read of the project; called once when the window has loaded.</summary>
        public Task InitializeAsync() => RefreshAsync();

        private async Task RefreshAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            try
            {
                IReadOnlyList<ModelRef> models = await _models.GetModelsAsync();
                Structural.SetModels(models);
                Mep.SetModels(models);

                int links = models.Count(m => !m.IsHost);
                int unloaded = models.Count(m => !m.IsHost && !m.IsLoaded);
                ModelRef host = models.FirstOrDefault(m => m.IsHost);
                StatusLine = $"{host?.DisplayName ?? "No active model"} · {links} Revit link(s)"
                    + (unloaded > 0 ? $", {unloaded} not loaded" : string.Empty);

                if (links == 0)
                    ShowInfo("This project has no Revit links. You can still choose the active model on both sides.");
                else if (HasNotification && !NotificationIsError)
                    Notification = string.Empty;
            }
            catch (Exception ex)
            {
                StatusLine = "Could not read the project.";
                ShowError(ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ShowError(string message)
        {
            NotificationIsError = true;
            Notification = message;
        }

        private void ShowInfo(string message)
        {
            NotificationIsError = false;
            Notification = message;
        }
    }
}
