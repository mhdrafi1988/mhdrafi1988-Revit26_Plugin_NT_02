using System;
using System.Collections.Generic;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace Revit26_Plugin_Revised.Shared.Services
{
    /// <summary>
    /// Lets a tool window listen to Revit events without ever removing a handler from Revit.
    /// <para>
    /// Revit allows adding or removing an event handler only inside its API context: a command's
    /// Execute, an external event, or a Revit event callback. A modeless window closes outside that
    /// context, so a <c>-=</c> in its Closing / Closed handler throws "Revit is currently not within
    /// an API context".
    /// </para>
    /// <para>
    /// The relay hooks each Revit event once per Revit session, the first time a tool subscribes,
    /// and forwards it to the tools listening at that moment. Disposing a subscription only removes
    /// the tool from the relay's own list, so it is safe anywhere, including a window's Closing handler.
    /// </para>
    /// <para>
    /// Subscribe from a command's Execute or from a constructor that Execute runs (an API context):
    /// the first subscription to each event attaches the relay to Revit.
    /// </para>
    /// </summary>
    public static class RevitEventRelay
    {
        private static readonly Channel<DocumentChangedEventArgs> DocumentChangedChannel = new();
        private static readonly Channel<DocumentClosingEventArgs> DocumentClosingChannel = new();
        private static readonly Channel<ViewActivatedEventArgs> ViewActivatedChannel = new();

        /// <summary>
        /// Calls <paramref name="listener"/> on <see cref="Application.DocumentChanged"/> until the returned
        /// subscription is disposed. Call from an API context.
        /// </summary>
        /// <param name="app">The Revit application (for example <c>document.Application</c>).</param>
        /// <param name="owner">A type of the listening tool; names the tool if the listener throws.</param>
        /// <param name="listener">Runs on Revit's main thread, inside the event.</param>
        public static IDisposable DocumentChanged(Application app, Type owner,
            EventHandler<DocumentChangedEventArgs> listener)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            return DocumentChangedChannel.Add(owner, listener, () => app.DocumentChanged += DocumentChangedChannel.Forward);
        }

        /// <summary>
        /// Calls <paramref name="listener"/> on <see cref="Application.DocumentClosing"/> until the returned
        /// subscription is disposed. Call from an API context.
        /// </summary>
        /// <param name="app">The Revit application.</param>
        /// <param name="owner">A type of the listening tool; names the tool if the listener throws.</param>
        /// <param name="listener">Runs on Revit's main thread, inside the event.</param>
        public static IDisposable DocumentClosing(Application app, Type owner,
            EventHandler<DocumentClosingEventArgs> listener)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            return DocumentClosingChannel.Add(owner, listener, () => app.DocumentClosing += DocumentClosingChannel.Forward);
        }

        /// <summary>
        /// Calls <paramref name="listener"/> on <see cref="UIApplication.ViewActivated"/> until the returned
        /// subscription is disposed. Call from an API context.
        /// </summary>
        /// <param name="app">The Revit UI application.</param>
        /// <param name="owner">A type of the listening tool; names the tool if the listener throws.</param>
        /// <param name="listener">Runs on Revit's main thread, inside the event.</param>
        public static IDisposable ViewActivated(UIApplication app, Type owner,
            EventHandler<ViewActivatedEventArgs> listener)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            return ViewActivatedChannel.Add(owner, listener, () => app.ViewActivated += ViewActivatedChannel.Forward);
        }

        /// <summary>One Revit event: the listeners, and whether the relay is attached to Revit yet.</summary>
        private sealed class Channel<TArgs> where TArgs : EventArgs
        {
            private readonly object _gate = new();
            private readonly List<Subscription> _listeners = new();
            private bool _attached;

            public IDisposable Add(Type owner, EventHandler<TArgs> listener, Action attach)
            {
                if (listener == null) throw new ArgumentNullException(nameof(listener));

                lock (_gate)
                {
                    if (!_attached)
                    {
                        // Throws outside an API context; nothing is registered in that case.
                        attach();
                        _attached = true;
                    }

                    var subscription = new Subscription(this, owner, listener);
                    _listeners.Add(subscription);
                    return subscription;
                }
            }

            /// <summary>The handler attached to Revit. Never lets a listener's exception reach Revit.</summary>
            public void Forward(object sender, TArgs e)
            {
                Subscription[] current;
                lock (_gate) current = _listeners.ToArray();

                foreach (var s in current)
                {
                    if (s.IsDisposed) continue;
                    try
                    {
                        s.Listener(sender, e);
                    }
                    catch (Exception ex)
                    {
                        // Stop calling a failing listener, so one bug does not show an error on every event.
                        s.Dispose();
                        ToolGuard.Report(s.Owner ?? typeof(RevitEventRelay), ex);
                    }
                }
            }

            private void Remove(Subscription subscription)
            {
                lock (_gate) _listeners.Remove(subscription);
            }

            /// <summary>A listener's registration. Dispose is safe anywhere and more than once.</summary>
            private sealed class Subscription : IDisposable
            {
                private readonly Channel<TArgs> _channel;

                public Subscription(Channel<TArgs> channel, Type owner, EventHandler<TArgs> listener)
                {
                    _channel = channel;
                    Owner = owner;
                    Listener = listener;
                }

                public Type Owner { get; }
                public EventHandler<TArgs> Listener { get; }
                public bool IsDisposed { get; private set; }

                public void Dispose()
                {
                    if (IsDisposed) return;
                    IsDisposed = true;
                    _channel.Remove(this);
                }
            }
        }
    }
}
