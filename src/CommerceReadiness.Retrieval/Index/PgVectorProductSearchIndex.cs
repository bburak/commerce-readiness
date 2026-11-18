using System.Text;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Retrieval;
using Npgsql;
using Pgvector;

namespace CommerceReadiness.Retrieval.Index;

/// <summary>
/// PostgreSQL + pgvector index with exact cosine search. The demo corpus is intentionally small, so exact search is
/// preferable; approximate vector indexing (HNSW/IVFFlat) would only be justified at larger scale.
/// Rows are built only from published observable data.
/// </summary>
public sealed class PgVectorProductSearchIndex : IProductSearchIndex, IAsyncDisposable
{
    public const string SchemaSql = """
        CREATE EXTENSION IF NOT EXISTS vector;
        CREATE TABLE IF NOT EXISTS product_search_documents (
            store_version        text          NOT NULL,
            embedding_model      text          NOT NULL,
            embedding_dimensions integer       NOT NULL,
            sku                  text          NOT NULL,
            product_id           text          NOT NULL,
            category             text          NOT NULL,
            title                text          NOT NULL,
            brand                text,
            semantic_text        text          NOT NULL,
            content_hash         text          NOT NULL,
            price_try            numeric(12,2) NOT NULL,
            available_quantity   integer       NOT NULL,
            shoe_size            integer,
            color                text,
            laptop_size_in       numeric(4,1),
            capacity_liters      numeric(5,1),
            battery_hours        integer,
            warranty_months      integer,
            embedding            vector        NOT NULL,
            PRIMARY KEY (store_version, embedding_model, embedding_dimensions, sku)
        );
        """;

    private const string Partition = "store_version = @store_version AND embedding_model = @model AND embedding_dimensions = @dimensions";

    private readonly string _connectionString;
    private readonly NpgsqlDataSource _dataSource;

    public PgVectorProductSearchIndex(string connectionString)
    {
        _connectionString = connectionString;
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        _dataSource = builder.Build();
    }

    public string Name => "pgvector";

    /// <summary>Idempotent: creates the vector extension and the table if missing.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // A plain connection: the vector type must exist before the mapped data source loads its types.
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(SchemaSql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var mapped = await _dataSource.OpenConnectionAsync(cancellationToken);
        await mapped.ReloadTypesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetContentHashesAsync(IndexPartition partition, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand($"SELECT sku, content_hash FROM product_search_documents WHERE {Partition}");
        AddPartition(command, partition);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            hashes[reader.GetString(0)] = reader.GetString(1);
        }

        return hashes;
    }

    public async Task UpsertAsync(IndexPartition partition, IReadOnlyList<IndexEntry> entries, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var entry in entries)
        {
            var sql = entry.Embedding is null
                ? $"""
                   UPDATE product_search_documents SET
                       product_id = @product_id, category = @category, title = @title, brand = @brand,
                       semantic_text = @semantic_text, content_hash = @content_hash, price_try = @price_try,
                       available_quantity = @available_quantity, shoe_size = @shoe_size, color = @color,
                       laptop_size_in = @laptop_size_in, capacity_liters = @capacity_liters,
                       battery_hours = @battery_hours, warranty_months = @warranty_months
                   WHERE {Partition} AND sku = @sku
                   """
                : """
                  INSERT INTO product_search_documents (
                      store_version, embedding_model, embedding_dimensions, sku, product_id, category, title, brand,
                      semantic_text, content_hash, price_try, available_quantity, shoe_size, color,
                      laptop_size_in, capacity_liters, battery_hours, warranty_months, embedding)
                  VALUES (
                      @store_version, @model, @dimensions, @sku, @product_id, @category, @title, @brand,
                      @semantic_text, @content_hash, @price_try, @available_quantity, @shoe_size, @color,
                      @laptop_size_in, @capacity_liters, @battery_hours, @warranty_months, @embedding)
                  ON CONFLICT (store_version, embedding_model, embedding_dimensions, sku) DO UPDATE SET
                      product_id = EXCLUDED.product_id, category = EXCLUDED.category, title = EXCLUDED.title,
                      brand = EXCLUDED.brand, semantic_text = EXCLUDED.semantic_text, content_hash = EXCLUDED.content_hash,
                      price_try = EXCLUDED.price_try, available_quantity = EXCLUDED.available_quantity,
                      shoe_size = EXCLUDED.shoe_size, color = EXCLUDED.color, laptop_size_in = EXCLUDED.laptop_size_in,
                      capacity_liters = EXCLUDED.capacity_liters, battery_hours = EXCLUDED.battery_hours,
                      warranty_months = EXCLUDED.warranty_months, embedding = EXCLUDED.embedding
                  """;

            await using var command = new NpgsqlCommand(sql, connection, transaction);
            AddPartition(command, partition);
            var d = entry.Document;
            command.Parameters.AddWithValue("sku", d.Sku);
            command.Parameters.AddWithValue("product_id", d.ProductId);
            command.Parameters.AddWithValue("category", d.Category.ToString());
            command.Parameters.AddWithValue("title", d.Title);
            command.Parameters.AddWithValue("brand", (object?)d.Brand ?? DBNull.Value);
            command.Parameters.AddWithValue("semantic_text", d.SemanticText);
            command.Parameters.AddWithValue("content_hash", d.ContentHash);
            command.Parameters.AddWithValue("price_try", d.PriceTry);
            command.Parameters.AddWithValue("available_quantity", d.AvailableQuantity);
            command.Parameters.AddWithValue("shoe_size", (object?)d.ShoeSize ?? DBNull.Value);
            command.Parameters.AddWithValue("color", (object?)d.Color ?? DBNull.Value);
            command.Parameters.AddWithValue("laptop_size_in", (object?)d.LaptopSizeIn ?? DBNull.Value);
            command.Parameters.AddWithValue("capacity_liters", (object?)d.CapacityLiters ?? DBNull.Value);
            command.Parameters.AddWithValue("battery_hours", (object?)d.BatteryHours ?? DBNull.Value);
            command.Parameters.AddWithValue("warranty_months", (object?)d.WarrantyMonths ?? DBNull.Value);
            if (entry.Embedding is { } embedding)
            {
                command.Parameters.AddWithValue("embedding", new Vector(embedding));
            }

            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected != 1)
            {
                throw new InvalidOperationException($"{d.Sku} has no stored embedding to keep.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(IndexPartition partition, IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand($"DELETE FROM product_search_documents WHERE {Partition} AND sku = ANY(@skus)");
        AddPartition(command, partition);
        command.Parameters.AddWithValue("skus", skus.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        IndexPartition partition, StructuredFilters? filters, float[]? queryEmbedding, int top, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand();
        AddPartition(command, partition);
        var where = new StringBuilder(Partition);
        if (filters is not null)
        {
            Filter(command, where, filters.Category?.ToString(), "category = @category", "category");
            Filter(command, where, filters.MaxPriceTry, "price_try <= @max_price", "max_price");
            if (filters.InStockOnly)
            {
                where.Append(" AND available_quantity > 0");
            }

            Filter(command, where, filters.ShoeSize, "shoe_size = @shoe_size", "shoe_size");
            Filter(command, where, filters.Color, "color = @color", "color");
            Filter(command, where, filters.MinimumLaptopSizeIn, "laptop_size_in >= @min_laptop", "min_laptop");
            Filter(command, where, filters.MinimumCapacityLiters, "capacity_liters >= @min_capacity", "min_capacity");
            Filter(command, where, filters.MinimumBatteryHours, "battery_hours >= @min_battery", "min_battery");
            Filter(command, where, filters.MinimumWarrantyMonths, "warranty_months >= @min_warranty", "min_warranty");
        }

        string similarity, order;
        if (queryEmbedding is null)
        {
            similarity = "NULL::double precision";
            order = "sku COLLATE \"C\"";
        }
        else
        {
            command.Parameters.AddWithValue("query", new Vector(queryEmbedding));
            similarity = "1 - (embedding <=> @query)";
            order = "embedding <=> @query, sku COLLATE \"C\"";
        }

        command.Parameters.AddWithValue("top", top);
        command.CommandText = $"""
            SELECT sku, product_id, title, category, price_try, available_quantity, shoe_size, color, {similarity} AS similarity
            FROM product_search_documents
            WHERE {where}
            ORDER BY {order}
            LIMIT @top
            """;

        var results = new List<SearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SearchResult(
                results.Count + 1,
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                Enum.Parse<Category>(reader.GetString(3)),
                reader.GetDecimal(4),
                reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8)));
        }

        return results;
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private static void AddPartition(NpgsqlCommand command, IndexPartition partition)
    {
        command.Parameters.AddWithValue("store_version", partition.StoreVersion);
        command.Parameters.AddWithValue("model", partition.EmbeddingModel);
        command.Parameters.AddWithValue("dimensions", partition.EmbeddingDimensions);
    }

    private static void Filter<T>(NpgsqlCommand command, StringBuilder where, T? value, string clause, string parameter)
    {
        if (value is null)
        {
            return;
        }

        where.Append(" AND ").Append(clause);
        command.Parameters.AddWithValue(parameter, value);
    }
}
