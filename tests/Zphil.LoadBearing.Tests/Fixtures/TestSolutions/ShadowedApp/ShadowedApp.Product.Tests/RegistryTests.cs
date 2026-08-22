namespace ShadowedApp.Product.Tests
{
    // Two fields, two controls: the stand-in this project declares itself, and the product type it genuinely
    // references. The first must resolve to the declaration, the second must survive as a project edge.
    public class RegistryTests
    {
        private Microsoft.Extensions.DependencyInjection.ServiceDescriptor stub;
        private ShadowedApp.Product.Registry subject;
    }
}
