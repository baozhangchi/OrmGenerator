using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OrmGenerator.DbProviders;

namespace OrmGenerator;

[Generator]
public class DbGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterImplementationSourceOutput(
            context.AnalyzerConfigOptionsProvider.Combine(
                context.CompilationProvider.Combine(context.AdditionalTextsProvider
                    .Where(x => Path.GetFileName(x.Path) == "DbSettings.json").Collect())),
            (productionContext, provider) =>
            {
                var configOptions = provider.Left;
                var compilation = provider.Right.Left;
                var additionalTexts = provider.Right.Right;
                if (configOptions.GlobalOptions.TryGetValue("build_property.rootnamespace", out var rootNamespace))
                {
                    GenerateBaseTypes(productionContext, rootNamespace, compilation);

                    foreach (var additionalText in additionalTexts)
                    {
                        var json = additionalText.GetText()?.ToString();
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            var settings = JsonConvert.DeserializeObject<JObject>(json!);
                            if (settings == null)
                            {
                                continue;
                            }

                            var dbType = settings.Value<string>("db_type");
                            if (string.IsNullOrWhiteSpace(dbType))
                            {
                                continue;
                            }

                            var connectionString = settings.Value<string>("connection_string");
                            if (string.IsNullOrWhiteSpace(connectionString))
                            {
                                return;
                            }

                            Generator dbProvider = null;
                            switch (dbType.ToLower())
                            {
                                case "mysql":
                                    dbProvider = Generator.CreateInstance<MySqlDbProvider>(settings, connectionString,
                                        productionContext, compilation);
                                    break;
                            }

                            if (dbProvider != null)
                            {
                                dbProvider.GenerateCode(rootNamespace);
                            }
                        }
                    }
                }
            });
    }

    private void GenerateBaseTypes(SourceProductionContext productionContext, string rootNamespace,
        Compilation compilation)
    {
    }
}

public class TableInfo
{
    public string TableName { get; set; }

    public List<ColumnInfo> Columns { get; } = new();
}

public class ColumnInfo
{
    public string ColumnName { get; set; }
    public int Order { get; set; }
    public bool IsNullable { get; set; }
    public bool IsKey { get; set; }
    public string Comment { get; set; }
    public string DataType { get; set; }
    public int MaxLength { get; set; }
    public bool IsIdentity { get; set; }

    public Type PropertyType
    {
        get
        {
            var propertyType = typeof(string);
            switch (DataType.ToLower())
            {
                case "integer":
                    propertyType = typeof(int);
                    break;
                case "datetime":
                    propertyType = typeof(DateTime);
                    break;
                case "decimal":
                    propertyType = typeof(decimal);
                    break;
                case "bigint":
                    propertyType = typeof(long);
                    break;
                case "double":
                    propertyType = typeof(double);
                    break;
            }


            if (IsNullable && propertyType != typeof(string) && propertyType.IsValueType)
            {
                propertyType = typeof(Nullable<>).MakeGenericType(propertyType);
            }

            return propertyType;
        }
    }
}