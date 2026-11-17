using System.Text.Json;
using System.Text.Json.Serialization;

namespace WaNut.Core.Bridge;

/// <summary>Writes enum values as camelCase strings ("plainText"); generated serializers need a parameterless converter.</summary>
internal sealed class CamelCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase)
    where TEnum : struct, Enum;
