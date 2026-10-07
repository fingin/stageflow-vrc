using System.ComponentModel;
using System.Globalization;
namespace StageFlow.App;

internal sealed class LiveProperties(string name) : CustomTypeDescriptor
{
    private readonly List<PropertyDescriptor> _rows = [];
    public string Filter { get; set; } = "";
    public override string GetComponentName() => name;
    public override object GetPropertyOwner(PropertyDescriptor? pd) => this;
    public override TypeConverter GetConverter() => new ExpandableObjectConverter();
    public void Add(string key, string category, Type type, Func<object?> get, Action<object?>? set, string description = "", string[]? options = null, bool secret = false)
        => _rows.Add(new LiveProperty(key, category, type, get, set, description, options, secret));
    public void Include(string section, PropertyDescriptor property, object owner) => _rows.Add(new SearchProperty(section, property, owner));
    public override PropertyDescriptorCollection GetProperties() => GetProperties(null);
    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => new(_rows.Where(p =>
        string.IsNullOrWhiteSpace(Filter) || p.DisplayName.Contains(Filter, StringComparison.OrdinalIgnoreCase) || p.Category.Contains(Filter, StringComparison.OrdinalIgnoreCase) || p.Description.Contains(Filter, StringComparison.OrdinalIgnoreCase)).ToArray());
    private sealed class SearchProperty(string section, PropertyDescriptor property, object owner)
        : PropertyDescriptor(section + "." + property.Name, [new CategoryAttribute(section), new DescriptionAttribute(property.Description), property.Attributes[typeof(PasswordPropertyTextAttribute)] ?? PasswordPropertyTextAttribute.No])
    {
        public override string DisplayName => property.DisplayName;
        public override Type ComponentType => typeof(LiveProperties);
        public override Type PropertyType => property.PropertyType;
        public override TypeConverter Converter => property.Converter;
        public override object? GetEditor(Type editorBaseType) => property.GetEditor(editorBaseType);
        public override bool IsReadOnly => property.IsReadOnly;
        public override bool CanResetValue(object component) => false;
        public override object? GetValue(object? component) => property.GetValue(owner);
        public override void SetValue(object? component, object? value) => property.SetValue(owner, value);
        public override void ResetValue(object component) { }
        public override bool ShouldSerializeValue(object component) => false;
    }
    private sealed class LiveProperty(string key, string category, Type type, Func<object?> get, Action<object?>? set, string description, string[]? options, bool secret)
        : PropertyDescriptor(key, [new CategoryAttribute(category), new DescriptionAttribute(description), new PasswordPropertyTextAttribute(secret)])
    {
        public override string DisplayName => Name switch
        {
            "Url" => "Server URL", "BFrames" => "B-frames", "VideoBitrateKbps" => "Video bitrate (kbps)",
            "MaxBitrateKbps" => "Maximum bitrate (kbps)", "AudioBitrateKbps" => "Audio bitrate (kbps)",
            "KeyframeSeconds" => "Keyframe interval (seconds)", "BufferSeconds" => "Buffer size (seconds)",
            _ => System.Text.RegularExpressions.Regex.Replace(Name, "(?<=[a-z0-9])([A-Z])", " $1")
        };
        public override Type ComponentType => typeof(LiveProperties);
        public override Type PropertyType => type;
        public override bool IsReadOnly => set == null;
        public override bool CanResetValue(object component) => false;
        public override object? GetValue(object? component) => get();
        public override void SetValue(object? component, object? value) { set?.Invoke(value); OnValueChanged(component, EventArgs.Empty); }
        public override void ResetValue(object component) { }
        public override bool ShouldSerializeValue(object component) => false;
        public override TypeConverter Converter => options == null ? base.Converter : new ChoicesConverter(options);
        public override object? GetEditor(Type editorBaseType) =>
            Name is "TextColor" or "BackgroundColor" && editorBaseType == typeof(System.Drawing.Design.UITypeEditor)
                ? new ColorSettingEditor() : base.GetEditor(editorBaseType);
    }
    private sealed class ChoicesConverter(string[] choices) : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) => new(choices);
        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => value is string s && s == "(default)" ? null : base.ConvertFrom(context, culture, value);
    }
}
