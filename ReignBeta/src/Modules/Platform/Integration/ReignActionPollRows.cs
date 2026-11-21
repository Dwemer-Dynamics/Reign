#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Platform.Integration
{
    internal static class ReignActionPollRows
    {
        internal static List<T> Read<T>(JArray rows, Func<JObject, string, T?> parse,
            Action<int, string?, string> reject) where T : class
        {
            var actions = new List<T>(rows.Count);
            for (int index = 0; index < rows.Count; index++)
            {
                if (!(rows[index] is JObject row))
                {
                    reject(index, null, "invalid_row");
                    continue;
                }

                string? id = ReadId(row);
                if (id == null)
                {
                    reject(index, null, "invalid_action_identity");
                    continue;
                }

                if (row["type"] is JContainer || row["command"] is JContainer
                    || !IsOptionalInt(row["typeValue"])
                    || !IsOptionalFiniteFloat(row["acceptedDay"])
                    || !IsOptionalFiniteFloat(row["executeAfterDays"]))
                {
                    reject(index, id, "invalid_action_fields");
                    continue;
                }

                T? action;
                try
                {
                    action = parse(row, id);
                }
                catch (Exception ex) when (ex is JsonException || ex is ArgumentException
                    || ex is InvalidCastException || ex is FormatException || ex is OverflowException)
                {
                    reject(index, id, "invalid_action_fields");
                    continue;
                }

                if (action == null) reject(index, id, "unknown_action_type");
                else actions.Add(action);
            }

            return actions;
        }

        // Match the scalar/string conversions used by ParseActionRecord, but
        // reserve its defaults for missing or null wire fields.
        private static bool IsOptionalInt(JToken? token)
        {
            return token == null || token.Type == JTokenType.Null
                || int.TryParse(token.ToString(), out _);
        }

        private static bool IsOptionalFiniteFloat(JToken? token)
        {
            return token == null || token.Type == JTokenType.Null
                || (float.TryParse(token.ToString(), out float value)
                    && !float.IsNaN(value) && !float.IsInfinity(value));
        }

        private static string? ReadId(JObject row)
        {
            if (!TryReadIdField(row["actionId"], out string? actionId)
                || !TryReadIdField(row["serverActionId"], out string? serverActionId)
                || (actionId != null && serverActionId != null && actionId != serverActionId))
                return null;

            return actionId ?? serverActionId;
        }

        private static bool TryReadIdField(JToken? token, out string? id)
        {
            id = null;
            if (token == null || token.Type == JTokenType.Null) return true;
            if (token.Type != JTokenType.String) return false;

            string? value = (string?)token;
            if (value == null || string.IsNullOrWhiteSpace(value)) return true;
            if (value.Any(char.IsControl)) return false;
            id = value;
            return true;
        }
    }
}
