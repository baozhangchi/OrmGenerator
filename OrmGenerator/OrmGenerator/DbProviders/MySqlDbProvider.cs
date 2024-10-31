using System;
using System.Collections.Generic;
using System.Linq;
using MySqlConnector;

namespace OrmGenerator.DbProviders;

public class MySqlDbProvider : IDbProvider
{
    public string GetPropertyType(ColumnInfo column)
    {
        return "string";
    }

    public List<TableInfo> GetTableInfos(string connectionString, out string databaseName)
    {
        var connectionStringBuilder = new MySqlConnectionStringBuilder(connectionString);
        databaseName = connectionStringBuilder.Database;
        var connection = new MySqlConnection(connectionStringBuilder.ConnectionString);
        try
        {
            connection.Open();
            var sql = @"select
	TABLE_NAME ,
	COLUMN_NAME ,
	ORDINAL_POSITION ,
	IS_NULLABLE = 'NO' as IS_NULLABLE ,
	COLUMN_KEY = 'PRI' as COLUMN_KEY ,
	COLUMN_COMMENT ,
	DATA_TYPE ,
	CHARACTER_MAXIMUM_LENGTH,
	EXTRA ='auto_increment' as IsIdentity
from
	INFORMATION_SCHEMA.`COLUMNS`
where
	TABLE_SCHEMA = @dataBase
order by
	ORDINAL_POSITION desc";
            var command = new MySqlCommand(sql, connection);
            command.Parameters.Add(new MySqlParameter("@dataBase", connectionStringBuilder.Database));
            var reader = command.ExecuteReader();
            var tableInfos = new Dictionary<string, List<ColumnInfo>>();
            while (reader.Read())
            {
                var tableName = reader.GetString(0);
                var columnName = reader.GetString(1);
                var ordinalPosition = reader.GetInt32(2);
                var isNullable = reader.GetBoolean(3);
                var isPrimaryKey = reader.GetBoolean(4);
                var columnComment = reader.GetString(5);
                var dataType = reader.GetString(6);
                var characterMaximumLength = reader.GetValue(7) == DBNull.Value ? 0 : reader.GetInt32(7);
                var isIdentity = reader.GetBoolean(8);
                if (!tableInfos.TryGetValue(tableName, out var columnInfos))
                {
                    columnInfos = new List<ColumnInfo>();
                    tableInfos[tableName] = columnInfos;
                }

                columnInfos.Add(new ColumnInfo
                {
                    ColumnName = columnName,
                    Order = ordinalPosition,
                    IsNullable = isNullable,
                    IsKey = isPrimaryKey,
                    Comment = columnComment,
                    DataType = dataType,
                    MaxLength = characterMaximumLength,
                    IsIdentity = isIdentity
                });
            }

            return tableInfos.Keys.Select(x =>
            {
                var table = new TableInfo
                {
                    TableName = x
                };
                foreach (var columnInfo in tableInfos[x].OrderBy(y => y.Order))
                {
                    table.Columns.Add(columnInfo);
                }

                return table;
            }).ToList();
        }
        finally
        {
            connection.Close();
        }
    }
}