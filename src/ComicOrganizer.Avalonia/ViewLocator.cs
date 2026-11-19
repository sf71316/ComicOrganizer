using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ComicOrganizer.Avalonia.ViewModels;

namespace ComicOrganizer.Avalonia;

public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
        {
            return null;
        }

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type != null && Activator.CreateInstance(type) is Control control)
        {
            return control;
        }

        return new TextBlock { Text = $"找不到檢視：{name}" };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
