using CandyGo.Api.DTOs;
using CandyGo.Api.Infrastructure;
using Dapper;

namespace CandyGo.Api.Services;

public interface IProductService
{
    Task<IReadOnlyList<ProductDto>> GetActiveAsync();
    Task<IReadOnlyList<ProductDto>> GetAllAsync();
    Task<ProductDto?> GetByIdAsync(long id);
    Task<ProductDto> CreateAsync(UpsertProductRequest request);
    Task<ProductDto> UpdateAsync(long id, UpsertProductRequest request);
    Task DeleteAsync(long id);
}

public sealed class ProductService : IProductService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProductService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ProductDto>> GetActiveAsync()
    {
        await using var connection = _connectionFactory.CreateConnection();

        var data = await connection.QueryAsync<ProductDto>(@"
SELECT id,
       name,
       description,
       image_url,
       price_candycash,
       is_active,
       sort_order,
       created_at,
       updated_at
FROM cg.products
WHERE is_active = 1
ORDER BY sort_order ASC, name ASC");

        return data.ToList();
    }

    public async Task<IReadOnlyList<ProductDto>> GetAllAsync()
    {
        await using var connection = _connectionFactory.CreateConnection();

        var data = await connection.QueryAsync<ProductDto>(@"
SELECT id,
       name,
       description,
       image_url,
       price_candycash,
       is_active,
       sort_order,
       created_at,
       updated_at
FROM cg.products
ORDER BY is_active DESC, sort_order ASC, name ASC");

        return data.ToList();
    }

    public async Task<ProductDto?> GetByIdAsync(long id)
    {
        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<ProductDto>(@"
SELECT id,
       name,
       description,
       image_url,
       price_candycash,
       is_active,
       sort_order,
       created_at,
       updated_at
FROM cg.products
WHERE id = @Id",
            new { Id = id });
    }

    public async Task<ProductDto> CreateAsync(UpsertProductRequest request)
    {
        Validate(request);

        await using var connection = _connectionFactory.CreateConnection();

        var id = await connection.ExecuteScalarAsync<long>(@"
INSERT INTO cg.products (name, description, image_url, price_candycash, is_active, sort_order)
VALUES (@Name, @Description, @ImageUrl, @PriceCandyCash, @IsActive, @SortOrder);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            request);

        var created = await GetByIdAsync(id);
        if (created is null)
        {
            throw new InvalidOperationException("No se pudo recuperar el producto creado.");
        }

        return created;
    }

    public async Task<ProductDto> UpdateAsync(long id, UpsertProductRequest request)
    {
        Validate(request);

        await using var connection = _connectionFactory.CreateConnection();

        var affectedRows = await connection.ExecuteAsync(@"
UPDATE cg.products
SET name = @Name,
    description = @Description,
    image_url = @ImageUrl,
    price_candycash = @PriceCandyCash,
    is_active = @IsActive,
    sort_order = @SortOrder,
    updated_at = SYSUTCDATETIME()
WHERE id = @Id",
            new
            {
                Id = id,
                request.Name,
                request.Description,
                request.ImageUrl,
                request.PriceCandyCash,
                request.IsActive,
                request.SortOrder
            });

        if (affectedRows == 0)
        {
            throw new KeyNotFoundException("Producto no encontrado.");
        }

        var updated = await GetByIdAsync(id);
        if (updated is null)
        {
            throw new InvalidOperationException("No se pudo recuperar el producto actualizado.");
        }

        return updated;
    }

    public async Task DeleteAsync(long id)
    {
        await using var connection = _connectionFactory.CreateConnection();

        var affectedRows = await connection.ExecuteAsync(
            "DELETE FROM cg.products WHERE id = @Id",
            new { Id = id });

        if (affectedRows == 0)
        {
            throw new KeyNotFoundException("Producto no encontrado.");
        }
    }

    private static void Validate(UpsertProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("El nombre del producto es requerido.");
        }

        if (request.PriceCandyCash < 0)
        {
            throw new ArgumentException("El precio no puede ser negativo.");
        }
    }
}
