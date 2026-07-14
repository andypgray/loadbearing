using System;

namespace MyApp.Web;

public sealed class WebRouteAttribute(string path) : Attribute
{
    public string Path { get; } = path;
}
