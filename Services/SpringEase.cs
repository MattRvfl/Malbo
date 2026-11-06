using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace DynamicIsland
{
    /// <summary>
    /// Courbe « ressort » : dépassement amorti façon dynamic island.
    /// Les paramètres sont des DependencyProperties pour survivre au clonage / gel des animations.
    /// </summary>
    public class SpringEase : EasingFunctionBase
    {
        public static readonly DependencyProperty DampingProperty =
            DependencyProperty.Register(nameof(Damping), typeof(double), typeof(SpringEase), new PropertyMetadata(7.0));

        public static readonly DependencyProperty FrequencyProperty =
            DependencyProperty.Register(nameof(Frequency), typeof(double), typeof(SpringEase), new PropertyMetadata(9.0));

        public SpringEase() { EasingMode = EasingMode.EaseIn; }

        public double Damping
        {
            get => (double)GetValue(DampingProperty);
            set => SetValue(DampingProperty, value);
        }

        public double Frequency
        {
            get => (double)GetValue(FrequencyProperty);
            set => SetValue(FrequencyProperty, value);
        }

        protected override double EaseInCore(double normalizedTime)
        {
            double t = normalizedTime;
            if (t >= 1) return 1;
            return 1 - Math.Exp(-Damping * t) * Math.Cos(Frequency * t);
        }

        protected override Freezable CreateInstanceCore() => new SpringEase();
    }
}
