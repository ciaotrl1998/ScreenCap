using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ScreenCap.Core;

namespace ScreenCap.Capture
{
    public sealed class RegionSelector
    {
        private readonly List<SelectionWindow> _windows = new List<SelectionWindow>();
        private readonly TaskCompletionSource<Rectangle?> _completion = new TaskCompletionSource<Rectangle?>();
        private KeyboardHook _keyboardHook;
        private MouseHook _mouseHook;
        private SelectionWindow _dragWindow;
        private bool _finished;

        public Task<Rectangle?> Result => _completion.Task;

        public void Begin()
        {
            var monitors = Native.GetMonitors();
            if (monitors.Count == 0)
            {
                _completion.TrySetResult(null);
                return;
            }

            _keyboardHook = new KeyboardHook();
            _keyboardHook.EscapePressed += () =>
                Application.Current?.Dispatcher.BeginInvoke(new Action(OnCancelled));

            foreach (var monitor in monitors)
            {
                var window = new SelectionWindow(monitor);
                window.Selected += OnSelected;
                window.Cancelled += OnCancelled;
                _windows.Add(window);
                window.Show();
            }

            _mouseHook = new MouseHook();
            _mouseHook.Move += OnMouseMove;
            _mouseHook.LeftDown += OnMouseLeftDown;
            _mouseHook.LeftUp += OnMouseLeftUp;
            _mouseHook.RightDown += () =>
                Application.Current?.Dispatcher.BeginInvoke(new Action(OnCancelled));
        }

        private void OnMouseMove(System.Drawing.Point screen)
        {
            if (_finished)
            {
                return;
            }

            if (_dragWindow != null)
            {
                _dragWindow.ExternalMoveDrag(screen);
                return;
            }

            foreach (var window in _windows)
            {
                window.UpdateHover();
            }
        }

        private void OnMouseLeftDown(System.Drawing.Point screen)
        {
            if (_finished || _dragWindow != null)
            {
                return;
            }

            var target = _windows.FirstOrDefault(w => w.ContainsScreen(screen));
            if (target == null)
            {
                return;
            }

            foreach (var window in _windows)
            {
                if (!ReferenceEquals(window, target))
                {
                    window.ClearHoverExternal();
                }
            }

            _dragWindow = target;
            target.ExternalBeginDrag(screen);
        }

        private void OnMouseLeftUp(System.Drawing.Point screen)
        {
            if (_dragWindow == null)
            {
                return;
            }
            var target = _dragWindow;
            _dragWindow = null;
            target.ExternalEndDrag(screen);
        }

        private void OnSelected(Rectangle rectangle)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _dragWindow = null;
            _completion.TrySetResult(rectangle);
            CloseAll();
        }

        private void OnCancelled()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _dragWindow = null;
            _completion.TrySetResult(null);
            CloseAll();
        }

        public void Cancel()
        {
            OnCancelled();
        }

        private void CloseAll()
        {
            _mouseHook?.Dispose();
            _mouseHook = null;
            _keyboardHook?.Dispose();
            _keyboardHook = null;

            foreach (var window in _windows.ToArray())
            {
                try
                {
                    window.Close();
                }
                catch
                {
                }
            }
        }
    }
}
