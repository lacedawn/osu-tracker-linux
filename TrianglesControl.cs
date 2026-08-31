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
            StartAnimation();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            StopAnimation();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == IsVisibleProperty || change.Property == IsAnimatedProperty)
            {
                if (IsVisible && IsAnimated)
                    StartAnimation();
                else
                    StopAnimation();
            }
            else if (change.Property == TriangleCountProperty)
            {
                _initialized = false;
            }
        }

        private void StartAnimation()
        {
            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _timer.Tick += OnTick;
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

            if (delta <= 0 || delta > 0.1) delta = 0.016;

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
