namespace ShadowedApp.Product
{
    // The genuine package reference, and the whole defect in one field. Private on purpose: the reference is
    // real and mints a type edge, but it never reaches this assembly's surface, so the test project beside it
    // can declare a type of the same full name without a clash - which is exactly why the idiom exists.
    public class Registry
    {
        private Microsoft.Extensions.DependencyInjection.ServiceDescriptor descriptor;

        public string Describe()
        {
            return "registry";
        }
    }
}
