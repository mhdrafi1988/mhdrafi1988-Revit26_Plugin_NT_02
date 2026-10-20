using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.Shared.Services
{
    /// <summary>
    /// Signature of an <see cref="IExternalCommand.Execute"/> body, so it can be passed to
    /// <see cref="ToolGuard.RunCommand"/> as a method group.
    /// </summary>
    public delegate Result CommandBody(ExternalCommandData commandData, ref string message, ElementSet elements);

    /// <summary>
    /// Suite-wide safety net: no exception from this add-in may reach Revit unhandled.
    /// Every <see cref="IExternalCommand"/> and <see cref="IExternalEventHandler"/> routes its
    /// Execute through here; UI-thread exceptions raised by this assembly's windows are caught by
    /// <see cref="Install"/>. Each failure is written to the error log and shown to the user.
    /// Open Revit transactions are disposed (and so rolled back) by their <c>using</c> blocks
    /// as the exception unwinds, before it is caught here.
    /// </summary>
    public static class ToolGuard
    {
        private static readonly Assembly ThisAssembly = typeof(ToolGuard).Assembly;

        // File names this add-in has shipped under: Revit26_Plugin.dll before 5.0.0, Revit26_Plugin_Revised.dll since.
        private static readonly string[] PluginAssemblyNames = { "Revit26_Plugin", "Revit26_Plugin_Revised" };
        private static readonly Regex BuildSuffix = new(@"\s*[—-]\s*(V|VA|FX|WSEB)\d+\s*$", RegexOptions.Compiled);
        private static bool _installed;

        /// <summary>Folder holding the daily error logs: %AppData%\Revit26_Plugin\Logs.</summary>
        public static string LogFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Revit26_Plugin", "Logs");

        /// <summary>
        /// Runs a command body. Cancellation returns <see cref="Result.Cancelled"/>; any other
        /// exception is logged, shown, and returned as <see cref="Result.Failed"/>.
        /// </summary>
        public static Result RunCommand(Type commandType, ExternalCommandData commandData, ref string message,
            ElementSet elements, CommandBody body)
        {
            try
            {
                return body(commandData, ref message, elements);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Report(commandType, ex);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Runs an external-event handler body; any exception is logged and shown instead of
        /// propagating into Revit's idling loop.
        /// </summary>
        public static void RunHandler(Type handlerType, UIApplication app, Action<UIApplication> body)
        {
            try
            {
                body(app);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Report(handlerType, ex);
            }
        }

        /// <summary>
        /// Call once from <c>OnStartup</c>. Catches unhandled UI-thread exceptions thrown by this
        /// assembly's code (window events, view-model commands) so a tool error never crashes Revit,
        /// and appends each tool's version to its window titles.
        /// </summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            Dispatcher.CurrentDispatcher.UnhandledException += OnDispatcherUnhandledException;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded));
        }

        /// <summary>
        /// Writes <paramref name="ex"/> to the error log and shows it to the user, naming the tool
        /// that owns <paramref name="source"/>. Never throws.
        /// </summary>
        public static void Report(Type source, Exception ex)
        {
            ToolInfo tool = ToolCatalog.Find(source);
            string toolTitle = tool?.Title ?? "SloperPro";

            // A second copy of the add-in in Revit is the usual cause of "Cannot find resource" in a
            // tool window: WPF can load the window's styles from the other, older DLL.
            var copies = OtherLoadedCopies();
            string advice = copies.Count == 0 ? null : OtherCopiesAdvice(copies);
            string logPath = WriteLog(toolTitle, source, ex, advice);

            try
            {
                var dialog = new TaskDialog(tool?.Name ?? "SloperPro")
                {
                    MainIcon = TaskDialogIcon.TaskDialogIconError,
                    MainInstruction = "The tool hit an error and stopped. The change in progress was rolled back.",
                    MainContent = advice == null ? ex.Message : ex.Message + "\n\n" + advice,
                    ExpandedContent = ex.ToString(),
                    FooterText = logPath == null ? toolTitle : $"{toolTitle} · Log: {logPath}"
                };
                dialog.Show();
            }
            catch
            {
                // Showing the dialog is best effort; the log already holds the details.
            }
        }

        /// <summary>
        /// Other copies of this add-in loaded in Revit, as "Name version — path". Empty when this is the
        /// only one. Never throws.
        /// </summary>
        public static IReadOnlyList<string> OtherLoadedCopies()
        {
            var copies = new List<string>();
            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly == ThisAssembly) continue;
                    AssemblyName name = assembly.GetName();
                    if (!PluginAssemblyNames.Contains(name.Name, StringComparer.OrdinalIgnoreCase)) continue;

                    string location;
                    try { location = string.IsNullOrEmpty(assembly.Location) ? "(loaded from memory)" : assembly.Location; }
                    catch (NotSupportedException) { location = "(unknown location)"; }
                    copies.Add($"{name.Name} {name.Version} — {location}");
                }
            }
            catch
            {
                // Listing loaded assemblies is diagnostics only.
            }
            return copies;
        }

        /// <summary>
        /// Call once Revit has loaded every add-in (ApplicationInitialized): when another copy of this
        /// add-in is loaded too, shows which file to remove. Never throws.
        /// </summary>
        public static void WarnIfOtherCopiesLoaded()
        {
            try
            {
                var copies = OtherLoadedCopies();
                if (copies.Count == 0) return;

                string advice = OtherCopiesAdvice(copies);
                WriteLog("SloperPro", typeof(ToolGuard), null, advice);
                new TaskDialog("SloperPro")
                {
                    MainIcon = TaskDialogIcon.TaskDialogIconWarning,
                    MainInstruction = "Two copies of SloperPro are loaded in Revit.",
                    MainContent = advice,
                    FooterText = "This copy: " + SafeLocation(ThisAssembly)
                }.Show();
            }
            catch
            {
                // The warning is best effort; the error dialogs repeat it if a tool fails.
            }
        }

        private static string OtherCopiesAdvice(IReadOnlyList<string> copies) =>
            "Another copy of SloperPro is loaded in Revit. Tool windows can pick up its old styles and fail " +
            "to open (\"Cannot find resource …\"). Other copies:\n" +
            string.Join("\n", copies.Select(c => "• " + c)) +
            "\n\nClose Revit, delete the .addin file that loads the other copy (in " +
            "C:\\ProgramData\\Autodesk\\Revit\\Addins\\2026 or %AppData%\\Autodesk\\Revit\\Addins\\2026) and its DLL, keep only " +
            "rf12_Revit_2026.addin, then start Revit again. This copy: " + SafeLocation(ThisAssembly);

        private static string SafeLocation(Assembly assembly)
        {
            try { return string.IsNullOrEmpty(assembly.Location) ? "(loaded from memory)" : assembly.Location; }
            catch { return "(unknown location)"; }
        }

        private static string WriteLog(string toolTitle, Type source, Exception ex, string note = null)
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
                string path = Path.Combine(LogFolder, $"errors-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {toolTitle} ({source?.FullName}){Environment.NewLine}" +
                    (ex == null ? string.Empty : $"{ex}{Environment.NewLine}") +
                    (note == null ? string.Empty : $"{note}{Environment.NewLine}") +
                    Environment.NewLine);
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Only handle exceptions raised by this add-in; Revit's own (and other add-ins') stay theirs.
            Type source = FindOwnFrameType(e.Exception);
            if (source == null) return;

            e.Handled = true;
            Report(source, e.Exception);
        }

        private static Type FindOwnFrameType(Exception ex)
        {
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                foreach (StackFrame frame in new StackTrace(current, false).GetFrames() ?? Array.Empty<StackFrame>())
                {
                    Type type = frame.GetMethod()?.DeclaringType;
                    if (type?.Assembly == ThisAssembly && type != typeof(ToolGuard))
                        return type;
                }
            }
            return null;
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Window window || window.GetType().Assembly != ThisAssembly) return;

                ToolInfo tool = ToolCatalog.Find(window.GetType());
                if (tool == null) return;

                string suffix = $" — v{tool.Version}";
                string title = window.Title ?? string.Empty;
                if (title.EndsWith(suffix, StringComparison.Ordinal)) return;

                // "Sheet Auto Rearrange — V026" becomes "Sheet Auto Rearrange — v26.0.0".
                title = BuildSuffix.Replace(title, string.Empty);
                window.Title = string.IsNullOrWhiteSpace(title) ? tool.Title : title + suffix;
            }
            catch
            {
                // Title decoration must never break a tool window.
            }
        }
    }
}
