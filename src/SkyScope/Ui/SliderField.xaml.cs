using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace SkyScope.Ui;

public partial class SliderField : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(SliderField), new PropertyMetadata(""));

    public static readonly DependencyProperty DescProperty =
        DependencyProperty.Register(nameof(Desc), typeof(string), typeof(SliderField), new PropertyMetadata(""));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(SliderField),
            new PropertyMetadata(0.0, OnRangeChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(SliderField),
            new PropertyMetadata(100.0, OnRangeChanged));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(double), typeof(SliderField),
            new PropertyMetadata(1.0, OnRangeChanged));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(SliderField),
            new PropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty DecimalsProperty =
        DependencyProperty.Register(nameof(Decimals), typeof(int), typeof(SliderField), new PropertyMetadata(0));

    public static readonly DependencyProperty SuffixProperty =
        DependencyProperty.Register(nameof(Suffix), typeof(string), typeof(SliderField), new PropertyMetadata(""));

    public static readonly DependencyProperty ScaleProperty =
        DependencyProperty.Register(nameof(Scale), typeof(double), typeof(SliderField),
            new PropertyMetadata(1.0, (_, _) => { }));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Desc
    {
        get => (string)GetValue(DescProperty);
        set => SetValue(DescProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Decimals
    {
        get => (int)GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    public string Suffix
    {
        get => (string)GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    public double Scale
    {
        get => (double)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public event RoutedPropertyChangedEventHandler<double>? ValueChanged;

    private bool _suppress;

    public SliderField()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncRange();
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SliderField field && field.IsLoaded) field.SyncRange();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SliderField field)
        {
            field.UpdateValueText();
            if (!field._suppress) field.ValueChanged?.Invoke(field, new RoutedPropertyChangedEventArgs<double>((double)e.OldValue, (double)e.NewValue));
        }
    }

    private void SyncRange()
    {
        Slider.Minimum = Minimum;
        Slider.Maximum = Maximum;
        Slider.TickFrequency = Step;
        Slider.SmallChange = Step;
        Slider.LargeChange = Step;
        SetSlider(Value);
    }

    public void SetValueSilently(double value)
    {
        Value = value;
        SetSlider(value);
        UpdateValueText();
    }

    private void SetSlider(double value)
    {
        _suppress = true;
        Slider.Value = Math.Clamp(value, Minimum, Maximum);
        _suppress = false;
    }

    private void UpdateValueText()
    {
        ValueText.Text = (Value * Scale).ToString($"F{Decimals}") + Suffix;
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress) return;
        var snapped = Math.Round(e.NewValue / Step) * Step;
        Value = snapped;
        SetSlider(snapped);
        UpdateValueText();
    }
}
