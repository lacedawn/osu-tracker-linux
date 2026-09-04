using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Diagnostics;

namespace Circle_Tracker
{
    public class TrianglesControl : Control
    {
        public static readonly StyledProperty<IBrush> TriangleBrushProperty =
            AvaloniaProperty.Register<TrianglesControl, IBrush>(
                nameof(TriangleBrush),
                new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)));

        public static readonly StyledProperty<int> TriangleCountProperty =
            AvaloniaProperty.Register<TrianglesControl, int>(
                nameof(TriangleCount),
                16);

        public static readonly StyledProperty<double> VelocityProperty =
            AvaloniaProperty.Register<TrianglesControl, double>(
                nameof(Velocity),
                16.0);

        public static readonly StyledProperty<bool> IsAnimatedProperty =
            AvaloniaProperty.Register<TrianglesControl, bool>(
                nameof(IsAnimated),
                true);

        public static readonly StyledProperty<bool> DisableBackgroundAnimationsWhenUnfocusedProperty =
            AvaloniaProperty.Register<TrianglesControl, bool>(
                nameof(DisableBackgroundAnimationsWhenUnfocused),
                false);

        public IBrush TriangleBrush
        {
            get => GetValue(TriangleBrushProperty);
            set => SetValue(TriangleBrushProperty, value);
        }

        public int TriangleCount
        {
            get => GetValue(TriangleCountProperty);
            set => SetValue(TriangleCountProperty, value);
        }

        public double Velocity
        {
            get => GetValue(VelocityProperty);
            set => SetValue(VelocityProperty, value);
        }

        public bool IsAnimated
        {
            get => GetValue(IsAnimatedProperty);
            set => SetValue(IsAnimatedProperty, value);
        }

        public bool DisableBackgroundAnimationsWhenUnfocused
        {
            get => GetValue(DisableBackgroundAnimationsWhenUnfocusedProperty);
            set => SetValue(DisableBackgroundAnimationsWhenUnfocusedProperty, value);
        }

        public bool IsTimerRunning => _timer != null && _timer.IsEnabled;
        public TimeSpan TimerInterval => _timer?.Interval ?? TimeSpan.Zero;

        private struct Particle
        {
            public float X;
            public float Y;
            public float Size;
            public float Speed;
            public float Alpha;
        }

        private static readonly StreamGeometry _unitTriangle;
        private Particle[] _particles = Array.Empty<Particle>();
        private readonly Random _random = new();
        private DispatcherTimer? _timer;
        private readonly Stopwatch _stopwatch = new();
        private double _lastElapsedSeconds = 0;
        private bool _initialized = false;

        private Window? _window;
        private IDisposable? _windowStateSubscription;
        private bool _isWindowMinimized = false;
        private bool _isWindowDeactivated = false;

        private bool ShouldPauseWhenUnfocused =>
            DisableBackgroundAnimationsWhenUnfocused ||
            UserSettings.GlobalDisableBackgroundAnimationsWhenUnfocused;

        static TrianglesControl()
        {
            _unitTriangle = new StreamGeometry();
            using var ctx = _unitTriangle.Open();
            ctx.BeginFigure(new Point(0, 0), true);
            ctx.LineTo(new Point(-0.57735, 1.0));
            ctx.LineTo(new Point(0.57735, 1.0));
            ctx.EndFigure(true);

            AffectsRender<TrianglesControl>(TriangleBrushProperty);
        }

        public TrianglesControl()
        {
            ClipToBounds = true;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            AttachWindow(e.Root as Window ?? TopLevel.GetTopLevel(this) as Window);
            StartAnimation();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            DetachWindow();
            StopAnimation();
        }

        private void AttachWindow(Window? window)
        {
            DetachWindow();

            _window = window;
            if (_window == null) return;

            _isWindowMinimized = _window.WindowState == WindowState.Minimized;
            _windowStateSubscription = _window.GetObservable(Window.WindowStateProperty).Subscribe(OnWindowStateChanged);
            _window.Activated += OnWindowActivated;
            _window.Deactivated += OnWindowDeactivated;
        }

        private void DetachWindow()
        {
            _windowStateSubscription?.Dispose();
            _windowStateSubscription = null;

            if (_window != null)
            {
                _window.Activated -= OnWindowActivated;
                _window.Deactivated -= OnWindowDeactivated;
                _window = null;
            }
        }

        private void OnWindowStateChanged(WindowState state)
        {
            if (state == WindowState.Minimized)
            {
                _isWindowMinimized = true;
                StopAnimation();
            }
            else
            {
                _isWindowMinimized = false;
                if (IsVisible && IsAnimated && (!_isWindowDeactivated || !ShouldPauseWhenUnfocused))
                {
                    StartAnimation();
                }
            }
        }

        internal void HandleWindowActivated()
        {
            OnWindowActivated(this, EventArgs.Empty);
        }

        internal void HandleWindowDeactivated()
        {
            OnWindowDeactivated(this, EventArgs.Empty);
        }

        private void OnWindowActivated(object? sender, EventArgs e)
        {
            _isWindowDeactivated = false;
            if (_timer != null)
            {
                _timer.Interval = TimeSpan.FromMilliseconds(16);
            }

            if (!_isWindowMinimized && IsVisible && IsAnimated)
            {
                StartAnimation();
            }
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            _isWindowDeactivated = true;
            if (ShouldPauseWhenUnfocused)
            {
                StopAnimation();
            }
            else if (_timer != null)
            {
                _timer.Interval = TimeSpan.FromMilliseconds(100);
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == IsVisibleProperty || change.Property == IsAnimatedProperty)
            {
                if (IsVisible && IsAnimated && !_isWindowMinimized && (!_isWindowDeactivated || !ShouldPauseWhenUnfocused))
                    StartAnimation();
                else
                    StopAnimation();
            }
            else if (change.Property == DisableBackgroundAnimationsWhenUnfocusedProperty)
            {
                if (_isWindowDeactivated)
                {
                    if (ShouldPauseWhenUnfocused)
                        StopAnimation();
                    else if (IsVisible && IsAnimated && !_isWindowMinimized)
                        StartAnimation();
                }
            }
            else if (change.Property == TriangleCountProperty)
            {
                _initialized = false;
            }
        }

        private void StartAnimation()
        {
            if (_isWindowMinimized)
            {
                return;
            }

            if (_isWindowDeactivated && ShouldPauseWhenUnfocused)
            {
                return;
            }

            TimeSpan interval = _isWindowDeactivated
                ? TimeSpan.FromMilliseconds(100)
                : TimeSpan.FromMilliseconds(16);

            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = interval
                };
                _timer.Tick += OnTick;
            }
            else
            {
                _timer.Interval = interval;
            }

            if (!_timer.IsEnabled && IsVisible && IsAnimated)
            {
                _stopwatch.Restart();
                _lastElapsedSeconds = 0;
                _timer.Start();
            }
        }

        private void StopAnimation()
        {
            if (_timer != null && _timer.IsEnabled)
            {
                _timer.Stop();
                _stopwatch.Stop();
            }
        }

        private void InitializeParticles(double width, double height)
        {
            int count = TriangleCount;
            if (count <= 0) count = 24;

            if (_particles.Length != count)
            {
                _particles = new Particle[count];
            }

            for (int i = 0; i < count; i++)
            {
                _particles[i] = CreateParticle((float)width, (float)height, true);
            }

            _initialized = true;
        }

        private Particle CreateParticle(float width, float height, bool randomY)
        {
            float size = (float)(_random.NextDouble() * 16 + 12);
            float x = (float)(_random.NextDouble() * (width + size * 2) - size);
            float y = randomY ? (float)(_random.NextDouble() * (height + size)) : height + size;
            float speed = (float)(_random.NextDouble() * 0.4 + 0.7);
            float alpha = (float)(_random.NextDouble() * 0.35 + 0.55);

            return new Particle
            {
                X = x,
                Y = y,
                Size = size,
                Speed = speed,
                Alpha = alpha
            };
        }

        private void OnTick(object? sender, EventArgs e)
        {
            double elapsed = _stopwatch.Elapsed.TotalSeconds;
            double delta = elapsed - _lastElapsedSeconds;
            _lastElapsedSeconds = elapsed;

            if (delta <= 0 || delta > 0.25) delta = 0.016;

            double width = Bounds.Width;
            double height = Bounds.Height;

            if (width <= 0 || height <= 0) return;

            if (!_initialized || _particles.Length != TriangleCount)
            {
                InitializeParticles(width, height);
            }

            double baseVelocity = Velocity;
            for (int i = 0; i < _particles.Length; i++)
            {
                _particles[i].Y -= (float)(baseVelocity * _particles[i].Speed * delta);

                if (_particles[i].Y + _particles[i].Size < 0)
                {
                    _particles[i] = CreateParticle((float)width, (float)height, false);
                }
            }

            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            double width = Bounds.Width;
            double height = Bounds.Height;

            if (width <= 0 || height <= 0) return;

            if (!_initialized || _particles.Length != TriangleCount)
            {
                InitializeParticles(width, height);
            }

            var brush = TriangleBrush;
            for (int i = 0; i < _particles.Length; i++)
            {
                var p = _particles[i];
                using (context.PushTransform(Matrix.CreateScale(p.Size, p.Size) * Matrix.CreateTranslation(p.X, p.Y)))
                using (context.PushOpacity(p.Alpha))
                {
                    context.DrawGeometry(brush, null, _unitTriangle);
                }
            }
        }
    }
}
