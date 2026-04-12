using Microsoft.Data.SqlClient;

namespace CandyGo.Api.Infrastructure;

public interface IDbConnectionFactory
{
    SqlConnection CreateConnection();
}
