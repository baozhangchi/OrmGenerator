using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json.Linq;

namespace OrmGenerator.DbProviders;
#nullable disable

public interface IDbProvider
{
    List<TableInfo> GetTableInfos(string connectionString, out string databaseName);
    string GetPropertyType(ColumnInfo column);
}

public class Generator
{
    private readonly Compilation _compilation;
    private readonly string _connectionString;
    private readonly IDbProvider _dbProvider;
    private readonly bool _generateEf6;
    private readonly bool _generateEfCore;
    private readonly bool _generateSqlSugar;
    private readonly SourceProductionContext _productionContext;
    private readonly JObject _settings;

    private Generator(IDbProvider dbProvider, JObject settings, string connectionString,
        SourceProductionContext productionContext,
        Compilation compilation)
    {
        _dbProvider = dbProvider;
        _settings = settings;
        _connectionString = connectionString;
        _productionContext = productionContext;
        _compilation = compilation;
        if (compilation.References.Any(x =>
                Path.GetFileName(x.Display) == "Microsoft.EntityFrameworkCore.dll"))
        {
            _generateEfCore = true;
        }

        if (compilation.References.Any(x =>
                Path.GetFileName(x.Display) == "EntityFramework.dll"))
        {
            _generateEf6 = true;
        }

        if (compilation.References.Any(x => Path.GetFileName(x.Display) == "SqlSugar.dll"))
        {
            _generateSqlSugar = true;
        }
    }

    public void GenerateCode(string rootNamespace)
    {
        var tables = _dbProvider.GetTableInfos(_connectionString, out var databaseName);
        ClassDeclarationSyntax efCoreDbContextClassDeclarationSyntax = null, ef6DbContextClassDeclarationSyntax = null;
        var entityNames = new List<string>();
        foreach (var table in tables)
        {
            var entityClassDeclarationSyntax = SyntaxFactory.ClassDeclaration(table.TableName)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.PartialKeyword));
            entityNames.Add(table.TableName);
            if (_generateEfCore || _generateEf6)
            {
                entityClassDeclarationSyntax =
                    entityClassDeclarationSyntax.AddAttribute(
                        "global::System.ComponentModel.DataAnnotations.Schema.Table", $"\"{table.TableName}\"");
            }

            if (_generateSqlSugar)
            {
                entityClassDeclarationSyntax =
                    entityClassDeclarationSyntax.AddAttribute("global::SqlSugar.SugarTable", $"\"{table.TableName}\"");
            }

            foreach (var column in table.Columns)
            {
                var propertyBuilder = new StringBuilder();
                propertyBuilder.AppendLine($@"/// <summary>
/// {column.Comment}
/// </summary>");
                propertyBuilder.AppendLine(
                    $"public {_dbProvider.GetPropertyType(column)} {column.ColumnName} {{ get; set; }}");
                var propertyDeclarationSyntax =
                    (PropertyDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(propertyBuilder.ToString())!;
                if (_generateEfCore || _generateEf6)
                {
                    if (column.IsKey)
                    {
                        propertyDeclarationSyntax =
                            propertyDeclarationSyntax.AddAttribute("global::System.ComponentModel.DataAnnotations.Key");
                    }

                    if (column.IsIdentity)
                    {
                        propertyDeclarationSyntax =
                            propertyDeclarationSyntax.AddAttribute(
                                "global::System.ComponentModel.DataAnnotations.Schema.DatabaseGenerated",
                                "global::System.ComponentModel.DataAnnotations.Schema.DatabaseGeneratedOption.Identity");
                    }

                    propertyDeclarationSyntax =
                        propertyDeclarationSyntax.AddAttribute(
                            "global::System.ComponentModel.DataAnnotations.Schema.Column", $"\"{column.ColumnName}\"");
                }

                if (_generateSqlSugar)
                {
                    var attributeArgumentList = new List<AttributeArgumentSyntax>();
                    attributeArgumentList.Add(
                        SyntaxFactory.AttributeArgument(
                            SyntaxFactory.ParseExpression($"ColumnName = \"{column.ColumnName}\"")));
                    attributeArgumentList.Add(
                        SyntaxFactory.AttributeArgument(
                            SyntaxFactory.ParseExpression($"ColumnDescription = \"{column.Comment}\"")));
                    attributeArgumentList.Add(
                        SyntaxFactory.AttributeArgument(
                            SyntaxFactory.ParseExpression($"IsNullable = {column.IsNullable.ToString().ToLower()}")));
                    attributeArgumentList.Add(
                        SyntaxFactory.AttributeArgument(
                            SyntaxFactory.ParseExpression($"IsPrimaryKey = {column.IsKey.ToString().ToLower()}")));
                    attributeArgumentList.Add(
                        SyntaxFactory.AttributeArgument(
                            SyntaxFactory.ParseExpression($"IsIdentity = {column.IsIdentity.ToString().ToLower()}")));
                    propertyDeclarationSyntax = propertyDeclarationSyntax.AddAttributeLists(
                        SyntaxFactory.AttributeList(
                            SyntaxFactory.SeparatedList(new List<AttributeSyntax>
                            {
                                SyntaxFactory.Attribute(SyntaxFactory.ParseName(@"global::SqlSugar.SugarColumn"),
                                    SyntaxFactory.AttributeArgumentList(
                                        SyntaxFactory.SeparatedList(attributeArgumentList)))
                            })));
                }

                entityClassDeclarationSyntax = entityClassDeclarationSyntax.AddMembers(propertyDeclarationSyntax);
            }

            var entityCode = entityClassDeclarationSyntax.ToFullCodeString(rootNamespace);
            _productionContext.AddSource($"{table.TableName}.g.cs", entityCode);
            _ = _compilation.AddSyntaxTrees(SyntaxFactory.ParseSyntaxTree(entityCode,
                _compilation.SyntaxTrees.First().Options));
        }
    }

    public static Generator CreateInstance<T>(JObject settings, string connectionString,
        SourceProductionContext productionContext,
        Compilation compilation)
        where T : IDbProvider, new()
    {
        return new Generator(new T(), settings, connectionString, productionContext, compilation);
    }
}