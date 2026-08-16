using Contoso.Json;

namespace FieldMini.Core
{
    // The one use site the whole bed turns on. It resolves to a real type only when the restore
    // succeeded; when it did not, the package edge behind it is absent from the model and the rule
    // forbidding it has nothing to measure.
    public class JsonUser
    {
        public string Describe(object value)
        {
            return JsonWriter.Write(value);
        }
    }
}
