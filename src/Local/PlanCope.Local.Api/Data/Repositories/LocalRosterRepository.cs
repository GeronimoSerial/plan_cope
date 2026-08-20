using Dapper;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Contracts.Sync;

namespace PlanCope.Local.Api.Data.Repositories;

public sealed class LocalRosterRepository(ILocalSqliteConnectionFactory connectionFactory) : ILocalRosterRepository
{
    public async Task<LocalRosterImportResult> ImportAsync(
        GeRosterPackageDto package,
        IDocumentHmacService documentHmacService,
        CancellationToken cancellationToken = default)
    {
        LocalRosterPackageValidator.Validate(package);

        using var connection = connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        var sameId = await connection.QuerySingleOrDefaultAsync<ExistingSnapshot>(new CommandDefinition(
            """
            SELECT id, checksum, section_count, student_count
            FROM local_roster_snapshots
            WHERE id = @SnapshotId
            LIMIT 1;
            """,
            new { SnapshotId = package.SnapshotId },
            transaction,
            cancellationToken: cancellationToken));
        if (sameId is not null && !string.Equals(sameId.Checksum, package.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A different checksum is already stored for this roster snapshot id.");
        }

        var existing = await connection.QuerySingleOrDefaultAsync<ExistingSnapshot>(new CommandDefinition(
            """
            SELECT id, checksum, section_count, student_count
            FROM local_roster_snapshots
            WHERE cue = @Cue AND school_year = @SchoolYear AND checksum = @Checksum
            LIMIT 1;
            """,
            new { package.Cue, package.SchoolYear, package.Checksum },
            transaction,
            cancellationToken: cancellationToken));
        if (existing is not null)
        {
            transaction.Commit();
            return new LocalRosterImportResult(false, existing.Id, existing.Checksum, existing.SectionCount, existing.StudentCount);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO local_roster_snapshots
                (id, cue, school_year, fetched_at, checksum, section_count, student_count, status)
            VALUES
                (@SnapshotId, @Cue, @SchoolYear, @FetchedAt, @Checksum, @SectionCount, @StudentCount, @Status)
            ON CONFLICT (cue, school_year, checksum) DO NOTHING;
            """,
            new
            {
                SnapshotId = package.SnapshotId,
                package.Cue,
                package.SchoolYear,
                FetchedAt = package.FetchedAt.ToString("O"),
                package.Checksum,
                SectionCount = package.Sections.Count,
                StudentCount = package.Sections.Sum(section => section.Students.Count),
                package.Status
            },
            transaction,
            cancellationToken: cancellationToken));

        var stored = await connection.QuerySingleOrDefaultAsync<ExistingSnapshot>(new CommandDefinition(
            """
            SELECT id, checksum, section_count, student_count
            FROM local_roster_snapshots
            WHERE cue = @Cue AND school_year = @SchoolYear AND checksum = @Checksum
            LIMIT 1;
            """,
            new { package.Cue, package.SchoolYear, package.Checksum },
            transaction,
            cancellationToken: cancellationToken));
        if (stored is null)
        {
            throw new InvalidOperationException("The roster snapshot could not be stored.");
        }

        if (!string.Equals(stored.Id, package.SnapshotId, StringComparison.Ordinal))
        {
            transaction.Commit();
            return new LocalRosterImportResult(false, stored.Id, stored.Checksum, stored.SectionCount, stored.StudentCount);
        }

        foreach (var section in package.Sections)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO local_roster_sections
                    (id, snapshot_id, ge_section_id, course, division, level, shift)
                VALUES
                    (@Id, @SnapshotId, @GeSectionId, @Course, @Division, @Level, @Shift);
                """,
                new
                {
                    section.Id,
                    SnapshotId = package.SnapshotId,
                    section.GeSectionId,
                    section.Course,
                    section.Division,
                    section.Level,
                    section.Shift
                },
                transaction,
                cancellationToken: cancellationToken));

            foreach (var student in section.Students)
            {
                var documentHash = documentHmacService.ComputeHash(student.Document);
                var documentLast4 = documentHmacService.ComputeLast4(student.Document);
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO local_roster_students
                        (id, snapshot_id, section_id, ge_person_id, document_hash, document_last4, first_name, last_name)
                    VALUES
                        (@Id, @SnapshotId, @SectionId, @GePersonId, @DocumentHash, @DocumentLast4, @FirstName, @LastName);
                    """,
                    new
                    {
                        student.Id,
                        SnapshotId = package.SnapshotId,
                        SectionId = student.SectionId,
                        student.GePersonId,
                        DocumentHash = documentHash,
                        DocumentLast4 = documentLast4,
                        student.FirstName,
                        student.LastName
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }
        }

        transaction.Commit();
        return new LocalRosterImportResult(
            true,
            package.SnapshotId,
            package.Checksum,
            package.Sections.Count,
            package.Sections.Sum(section => section.Students.Count));
    }

    public async Task<LocalRosterStudentLookup?> FindStudentAsync(
        string snapshotId,
        string sectionId,
        string document,
        IDocumentHmacService documentHmacService,
        CancellationToken cancellationToken = default)
    {
        var documentHash = documentHmacService.ComputeHash(document);
        using var connection = connectionFactory.CreateOpenConnection();
        return await connection.QuerySingleOrDefaultAsync<LocalRosterStudentLookup>(new CommandDefinition(
            """
            SELECT snapshot_id AS SnapshotId,
                   section_id AS SectionId,
                   id AS RosterStudentId,
                   ge_person_id AS GePersonId,
                   document_last4 AS DocumentLast4,
                   first_name AS FirstName,
                   last_name AS LastName
            FROM local_roster_students
            WHERE snapshot_id = @SnapshotId AND section_id = @SectionId AND document_hash = @DocumentHash
            LIMIT 1;
            """,
            new { SnapshotId = snapshotId, SectionId = sectionId, DocumentHash = documentHash },
            cancellationToken: cancellationToken));
    }

    public async Task<LocalRosterSnapshotLookup?> GetLatestSnapshotAsync(
        string cue,
        string schoolYear,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        return await connection.QuerySingleOrDefaultAsync<LocalRosterSnapshotLookup>(new CommandDefinition(
            """
            SELECT id AS Id,
                   cue AS Cue,
                   school_year AS SchoolYear,
                   fetched_at AS FetchedAt,
                   checksum AS Checksum,
                   section_count AS SectionCount,
                   student_count AS StudentCount,
                   status AS Status
            FROM local_roster_snapshots
            WHERE cue = @Cue AND school_year = @SchoolYear
            ORDER BY fetched_at DESC, id DESC
            LIMIT 1;
            """,
            new { Cue = cue.Trim().ToUpperInvariant(), SchoolYear = schoolYear.Trim() },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<LocalRosterSectionLookup>> GetSectionsAsync(
        string cue,
        string schoolYear,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var rows = await connection.QueryAsync<LocalRosterSectionLookup>(new CommandDefinition(
            """
            WITH latest AS (
                SELECT id
                FROM local_roster_snapshots
                WHERE cue = @Cue AND school_year = @SchoolYear
                ORDER BY fetched_at DESC, id DESC
                LIMIT 1
            )
            SELECT s.id AS Id,
                   s.snapshot_id AS SnapshotId,
                   s.ge_section_id AS GeSectionId,
                   s.course AS Course,
                   s.division AS Division,
                   s.level AS Level,
                   s.shift AS Shift,
                   COUNT(st.id) AS StudentCount
            FROM local_roster_sections s
            JOIN latest l ON l.id = s.snapshot_id
            LEFT JOIN local_roster_students st ON st.section_id = s.id AND st.snapshot_id = s.snapshot_id
            GROUP BY s.id, s.snapshot_id, s.ge_section_id, s.course, s.division, s.level, s.shift
            ORDER BY s.course, s.division, s.id;
            """,
            new { Cue = cue.Trim().ToUpperInvariant(), SchoolYear = schoolYear.Trim() },
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<LocalRosterSelectionValidation> ValidateSelectionAsync(
        string cue,
        string schoolYear,
        string snapshotId,
        string sectionId,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var snapshot = await connection.QuerySingleOrDefaultAsync<SelectionSnapshotRow>(new CommandDefinition(
            """
            SELECT id, cue, school_year, status
            FROM local_roster_snapshots
            WHERE id = @SnapshotId
            LIMIT 1;
            """,
            new { SnapshotId = snapshotId },
            cancellationToken: cancellationToken));

        if (snapshot is null)
        {
            return new(false, "El snapshot del padrón no existe en este equipo.");
        }

        if (!string.Equals(snapshot.Cue, cue.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(snapshot.SchoolYear, schoolYear.Trim(), StringComparison.Ordinal))
        {
            return new(false, "El snapshot no corresponde al CUE y ciclo lectivo seleccionados.");
        }

        if (!string.Equals(snapshot.Status, "Ready", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, "El snapshot del padrón no está disponible para crear sesiones.");
        }

        var sectionExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (
                SELECT 1
                FROM local_roster_sections
                WHERE id = @SectionId AND snapshot_id = @SnapshotId
            );
            """,
            new { SectionId = sectionId, SnapshotId = snapshotId },
            cancellationToken: cancellationToken));

        return sectionExists
            ? new(true, null)
            : new(false, "La sección no pertenece al snapshot seleccionado.");
    }

    private sealed class SelectionSnapshotRow
    {
        public string Id { get; init; } = string.Empty;
        public string Cue { get; init; } = string.Empty;
        public string SchoolYear { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
    }

    private sealed class ExistingSnapshot
    {
        public string Id { get; init; } = string.Empty;

        public string Checksum { get; init; } = string.Empty;

        public int SectionCount { get; init; }

        public int StudentCount { get; init; }
    }
}
