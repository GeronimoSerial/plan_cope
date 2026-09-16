using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PlanCope.TestSupport;

/// <summary>
/// Makes <see cref="PlanCope.Central.Api.Data.PlanCopeDbContext"/> buildable against EF Core's
/// InMemory provider for tests, by converting every <see cref="JsonDocument"/> property to a
/// string. Needed because several of those properties are also configured with
/// <c>HasColumnType("jsonb")</c> for Npgsql, which InMemory doesn't understand.
/// </summary>
/// <remarks>
/// The converter MUST be applied through the fluent <c>modelBuilder.Entity(...).Property(...)
/// .HasConversion(...)</c> API, not the mutable/convention-level
/// <c>IMutableProperty.SetValueConverter(...)</c> call. The convention-level call does not
/// survive model finalization when a relational <c>HasColumnType("jsonb")</c> is already
/// configured on the same property (as it is here) - the InMemory model validator still
/// rejects the property as unmapped. Re-applying it through the fluent builder, at the same
/// configuration-source precedence entity configurations use, does take effect. Shared between
/// PlanCope.Central.Api.Tests and PlanCope.E2E.Tests via a linked &lt;Compile Include&gt; - keep
/// this the single copy rather than re-adding a private nested version to either project.
/// </remarks>
internal sealed class JsonDocumentFriendlyModelCustomizer : ModelCustomizer
{
    public JsonDocumentFriendlyModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies)
    {
    }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            foreach (var property in entityType.GetProperties().ToList())
            {
                if (property.ClrType == typeof(JsonDocument))
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .Property(property.Name)
                        .HasConversion(JsonDocumentConverter.Instance);
                }
            }
        }
    }
}

internal sealed class JsonDocumentConverter : ValueConverter<JsonDocument, string>
{
    public static readonly JsonDocumentConverter Instance = new();

    private JsonDocumentConverter()
        : base(
            static document => document.RootElement.GetRawText(),
            static raw => JsonDocument.Parse(raw))
    {
    }
}
