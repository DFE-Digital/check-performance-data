using System.Globalization;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

public static class SchemaTypeConvertor
{
    public static void ApplySchemaTypes(JObject record, JSchema schema)
    {
        foreach (JProperty property in record.Properties().ToList())
        {
            if (!schema.Properties.TryGetValue(property.Name, out JSchema? propertySchema))
            {
                continue;
            }

            string? rawValue = property.Value.Type == JTokenType.Null
                ? null
                : property.Value.ToString();

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                if (propertySchema.Type?.HasFlag(JSchemaType.Null) == true)
                {
                    property.Value = JValue.CreateNull();
                }

                continue;
            }

            if (propertySchema.Type?.HasFlag(JSchemaType.Integer) == true
                && long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integerValue))
            {
                property.Value = integerValue;
                continue;
            }

            if (propertySchema.Type?.HasFlag(JSchemaType.Number) == true
                && decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture,
                    out decimal numberValue))
            {
                property.Value = numberValue;
                continue;
            }

            if (propertySchema.Type?.HasFlag(JSchemaType.Boolean) == true
                && bool.TryParse(rawValue, out bool booleanValue))
            {
                property.Value = booleanValue;
                continue;
            }

            if (propertySchema.Type?.HasFlag(JSchemaType.String) == true)
            {
                if (propertySchema.Format == "date"
                    && DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dateValue))
                {
                    property.Value = dateValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                else
                {
                    property.Value = rawValue;
                }
            }
        }

    }
}