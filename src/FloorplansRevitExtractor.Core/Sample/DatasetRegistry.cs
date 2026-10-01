using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FloorplansRevitExtractor.Core.Json;

namespace FloorplansRevitExtractor.Core.Sample
{
    /// <summary>
    /// registry.json (spec v1 section 9): sample key to id, so re-exporting a
    /// view reuses its id instead of creating a duplicate. Ids start at
    /// 900001, far from the synthetic 000001 range, so a real sample is
    /// obvious on sight and never collides. Immutable: Resolve returns a new
    /// registry when it assigns an id.
    /// </summary>
    public sealed class DatasetRegistry
    {
        public const int FirstId = 900001;
        public const int LastId = 999999;

        private readonly IReadOnlyDictionary<string, int> _ids;

        private DatasetRegistry(IReadOnlyDictionary<string, int> ids)
        {
            _ids = ids;
        }

        public int Count => _ids.Count;

        public static DatasetRegistry Load(string path)
        {
            if (!File.Exists(path))
            {
                return new DatasetRegistry(new Dictionary<string, int>());
            }

            try
            {
                using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(path));
                var ids = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (JsonElement entry in json.RootElement.GetProperty("samples").EnumerateArray())
                {
                    ids.Add(entry.GetProperty("key").GetString()!, int.Parse(entry.GetProperty("id").GetString()!));
                }

                return new DatasetRegistry(ids);
            }
            catch (Exception ex) when (ex is JsonException || ex is KeyNotFoundException || ex is InvalidOperationException
                                       || ex is FormatException || ex is ArgumentException)
            {
                // Starting over would hand out ids already used in the batch.
                throw new InvalidDataException($"{path} is not a valid registry: {ex.Message}", ex);
            }
        }

        public (DatasetRegistry Registry, int Id) Resolve(SampleKey key)
        {
            if (_ids.TryGetValue(key.ToString(), out int existing))
            {
                return (this, existing);
            }

            int next = _ids.Count == 0 ? FirstId : _ids.Values.Max() + 1;
            if (next > LastId)
            {
                throw new InvalidOperationException($"No sample ids left after {LastId}.");
            }

            var ids = new Dictionary<string, int>(_ids.ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal)
            {
                [key.ToString()] = next,
            };
            return (new DatasetRegistry(ids), next);
        }

        /// <summary>Atomic: a crash mid-write never leaves a half-written registry that blocks the batch.</summary>
        public void Save(string path) => SampleExporter.WriteJson(path, Write);

        private void Write(Stream stream)
        {
            using var writer = new Utf8JsonWriter(stream, JsonFields.Options);
            writer.WriteStartObject();
            writer.WriteStartArray("samples");
            foreach (KeyValuePair<string, int> entry in _ids.OrderBy(p => p.Value))
            {
                writer.WriteStartObject();
                writer.WriteString("id", SampleFiles.IdText(entry.Value));
                writer.WriteString("key", entry.Key);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
