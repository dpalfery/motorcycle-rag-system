using System.Collections;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class IngestionJobRepositoryTests : IDisposable
{
    public IngestionJobRepositoryTests()
    {
        IngestionJobRepository.InvalidateSchemaMetadataCache();
    }

    public void Dispose()
    {
        IngestionJobRepository.InvalidateSchemaMetadataCache();
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConnectionFactoryIsNull()
    {
        var act = () => new IngestionJobRepository(null!, NullLogger<IngestionJobRepository>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var factory = new Mock<ISqlConnectionFactory>();

        var act = () => new IngestionJobRepository(factory.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task CreateAsync_ShouldThrowArgumentNullException_WhenJobIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.CreateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("job");
    }

    [Fact]
    public async Task CreateAsync_ShouldSetSqlId_WhenLegacyIdColumnExists()
    {
        var job = CreateJob();
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(1);
        connection.EnqueueReader(
            CreateReader(new Dictionary<string, object?> { ["Id"] = 42L }),
            command =>
            {
                command.CommandText.Should().Contain("OUTPUT INSERTED.[Id]");
                command.Parameters["Status"].Should().Be(IngestionJobStatus.Queued.ToString());
                command.Parameters["InputType"].Should().Be(IngestionJobType.PDFManual.ToString());
                command.Parameters["InputRef"].Should().Be(job.InputRef);
            });

        var sut = CreateSut(connection);

        var result = await sut.CreateAsync(job);

        result.IngestionJobId.Should().Be(job.IngestionJobId);
        result.Id.Should().Be(42);
        connection.ExecutedCommands.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateAsync_ShouldLeaveSqlIdAtZero_WhenLegacyIdColumnDoesNotExist()
    {
        var job = CreateJob();
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(0);
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.CommandText.Should().NotContain("OUTPUT INSERTED.[Id]");
                command.Parameters["InputRef"].Should().Be(job.InputRef);
            });

        var sut = CreateSut(connection);

        var result = await sut.CreateAsync(job);

        result.Id.Should().Be(0);
        connection.ExecutedCommands.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateAsync_ShouldWrapConnectionFailures()
    {
        var factory = new Mock<ISqlConnectionFactory>();
        var expected = new InvalidOperationException("boom");
        factory.Setup(x => x.CreateOpenConnectionAsync()).ThrowsAsync(expected);
        var sut = new IngestionJobRepository(factory.Object, NullLogger<IngestionJobRepository>.Instance);

        var act = async () => await sut.CreateAsync(CreateJob());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().StartWith("Failed to create ingestion job ");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldMapReturnedRow()
    {
        var jobId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(1);
        connection.EnqueueReader(
            CreateReader(CreateJobRow(
                id: 7L,
                ingestionJobId: jobId,
                status: IngestionJobStatus.Completed,
                inputType: IngestionJobType.PDFManual,
                inputRef: "manuals/honda.pdf",
                currentStage: "completed",
                metadataJson: """{"make":"Honda"}""")),
            command =>
            {
                command.CommandText.Should().Contain("WHERE [IngestionJobId] = @IngestionJobId");
                command.Parameters["IngestionJobId"].Should().Be(jobId);
            });

        var sut = CreateSut(connection);

        var result = await sut.GetByIdAsync(jobId);

        result.Should().NotBeNull();
        result!.Id.Should().Be(7);
        result.IngestionJobId.Should().Be(jobId);
        result.Status.Should().Be(IngestionJobStatus.Completed);
        result.InputType.Should().Be(IngestionJobType.PDFManual);
        result.InputRef.Should().Be("manuals/honda.pdf");
        result.CurrentStage.Should().Be("completed");
        result.MetadataJson.Should().Be("""{"make":"Honda"}""");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenNoRowMatches()
    {
        // Arrange
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(1);
        connection.EnqueueReader(CreateReader());
        var sut = CreateSut(connection);

        // Act
        var result = await sut.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WhenConnectionFails_WrapsFailureWithJobIdentifier()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var expected = new InvalidOperationException("sql down");
        var sut = CreateThrowingSut(expected);

        // Act
        var act = () => sut.GetByIdAsync(jobId);

        // Assert
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be($"Failed to get ingestion job {jobId}");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReuseCachedSqlIdProbe()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(1);
        connection.EnqueueReader(CreateReader(CreateJobRow(ingestionJobId: firstId)));
        connection.EnqueueReader(CreateReader(CreateJobRow(ingestionJobId: secondId)));
        var sut = CreateSut(connection);

        var firstResult = await sut.GetByIdAsync(firstId);
        var secondResult = await sut.GetByIdAsync(secondId);

        firstResult.Should().NotBeNull();
        secondResult.Should().NotBeNull();
        connection.ExecutedCommands.Should().HaveCount(3);
        connection.ExecutedCommands.Count(command =>
            command.CommandText.Contains("COL_LENGTH('dbo.IngestionJobs', 'Id')", StringComparison.Ordinal))
            .Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistAllMutableFields()
    {
        var job = CreateJob(
            status: IngestionJobStatus.Processing,
            stageSetAtUtc: new DateTimeOffset(2026, 7, 10, 14, 0, 0, TimeSpan.Zero),
            pagesCapturedViewableCount: 3);
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.CommandText.Should().Contain("UPDATE [dbo].[IngestionJobs] SET");
                command.Parameters["Status"].Should().Be(IngestionJobStatus.Processing.ToString());
                command.Parameters["InputType"].Should().Be(IngestionJobType.PDFManual.ToString());
                command.Parameters["PagesCapturedViewableCount"].Should().Be(3);
            });

        var sut = CreateSut(connection);

        await sut.UpdateAsync(job);

        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Fact]
    public async Task UpdateAsync_ShouldWrapConnectionFailures()
    {
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.UpdateAsync(CreateJob());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().StartWith("Failed to update ingestion job ");
        exception.Which.InnerException.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldPersistStatusAndFailureReason()
    {
        var jobId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.Parameters["IngestionJobId"].Should().Be(jobId);
                command.Parameters["Status"].Should().Be(IngestionJobStatus.Failed.ToString());
                command.Parameters["FailureReason"].Should().Be("bad pdf");
            });

        var sut = CreateSut(connection);

        await sut.UpdateStatusAsync(jobId, IngestionJobStatus.Failed, "bad pdf");
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldWrapConnectionFailures()
    {
        var jobId = Guid.NewGuid();
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.UpdateStatusAsync(jobId, IngestionJobStatus.Failed);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be($"Failed to update status for ingestion job {jobId}");
    }

    [Fact]
    public async Task GetLatestByInputRefAsync_ShouldThrowArgumentException_WhenInputRefIsBlank()
    {
        var sut = CreateSut();

        var act = async () => await sut.GetLatestByInputRefAsync(" ");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("inputRef");
    }

    [Fact]
    public async Task GetLatestByInputRefAsync_ShouldReturnLatestMatchingJob()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(1);
        connection.EnqueueReader(
            CreateReader(CreateJobRow(inputRef: "manuals/yamaha.pdf", status: IngestionJobStatus.Completed)),
            command =>
            {
                command.CommandText.Should().Contain("WHERE [InputRef] = @InputRef");
                command.Parameters["InputRef"].Should().Be("manuals/yamaha.pdf");
            });
        var sut = CreateSut(connection);

        var result = await sut.GetLatestByInputRefAsync("manuals/yamaha.pdf");

        result.Should().NotBeNull();
        result!.InputRef.Should().Be("manuals/yamaha.pdf");
        result.Status.Should().Be(IngestionJobStatus.Completed);
    }

    [Fact]
    public async Task GetLatestByInputRefAsync_ShouldWrapConnectionFailures()
    {
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.GetLatestByInputRefAsync("manuals/yamaha.pdf");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get latest ingestion job by input ref");
    }

    [Fact]
    public async Task GetLatestByInputAsync_ShouldThrowArgumentException_WhenInputRefIsBlank()
    {
        var sut = CreateSut();

        var act = async () => await sut.GetLatestByInputAsync(" ", IngestionJobType.PDFManual);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("inputRef");
    }

    [Fact]
    public async Task GetLatestByInputAsync_ShouldFilterByInputType()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(1);
        connection.EnqueueReader(
            CreateReader(CreateJobRow(inputRef: "manuals/kawasaki.pdf", inputType: IngestionJobType.PDFManual)),
            command =>
            {
                command.Parameters["InputRef"].Should().Be("manuals/kawasaki.pdf");
                command.Parameters["InputType"].Should().Be(IngestionJobType.PDFManual.ToString());
            });
        var sut = CreateSut(connection);

        var result = await sut.GetLatestByInputAsync("manuals/kawasaki.pdf", IngestionJobType.PDFManual);

        result.Should().NotBeNull();
        result!.InputType.Should().Be(IngestionJobType.PDFManual);
    }

    [Fact]
    public async Task GetLatestByInputAsync_ShouldWrapConnectionFailures()
    {
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.GetLatestByInputAsync("manuals/kawasaki.pdf", IngestionJobType.PDFManual);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get latest ingestion job by input ref and type");
    }

    [Fact]
    public async Task GetLatestByInputRefsAsync_ShouldThrowArgumentNullException_WhenPairsAreNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.GetLatestByInputRefsAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("pairs");
    }

    [Fact]
    public async Task GetLatestByInputRefsAsync_ShouldReturnEmpty_WhenPairsAreEmpty()
    {
        var factory = new Mock<ISqlConnectionFactory>(MockBehavior.Strict);
        var sut = new IngestionJobRepository(factory.Object, NullLogger<IngestionJobRepository>.Instance);

        var result = await sut.GetLatestByInputRefsAsync(Array.Empty<(string InputRef, IngestionJobType InputType)>());

        result.Should().BeEmpty();
        factory.Verify(x => x.CreateOpenConnectionAsync(), Times.Never);
    }

    [Fact]
    public async Task GetLatestByInputRefsAsync_ShouldReturnLatestRowsForEachPair()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(1);
        connection.EnqueueReader(
            CreateReader(
                CreateJobRow(inputRef: "upload-1", inputType: IngestionJobType.PDFManual),
                CreateJobRow(inputRef: "upload-2", inputType: IngestionJobType.StructuredSpecification)),
            command =>
            {
                command.CommandText.Should().Contain("WITH RankedJobs AS");
                var values = command.Parameters.Values.OfType<string>().ToList();
                values.Should().Contain("upload-1");
                values.Should().Contain("upload-2");
                values.Should().Contain(IngestionJobType.PDFManual.ToString());
                values.Should().Contain(IngestionJobType.StructuredSpecification.ToString());
            });
        var sut = CreateSut(connection);

        var result = await sut.GetLatestByInputRefsAsync(
            [("upload-1", IngestionJobType.PDFManual), ("upload-2", IngestionJobType.StructuredSpecification)]);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetLatestByInputRefsAsync_ShouldWrapConnectionFailures()
    {
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.GetLatestByInputRefsAsync([("upload-1", IngestionJobType.PDFManual)]);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get latest ingestion jobs by input refs batch");
    }

    [Fact]
    public async Task GetRecentAsync_ShouldThrowArgumentOutOfRangeException_WhenMaxCountIsNotPositive()
    {
        var sut = CreateSut();

        var act = async () => await sut.GetRecentAsync(0);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("maxCount");
    }

    [Fact]
    public async Task GetRecentAsync_ShouldReturnMostRecentRows()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(1);
        connection.EnqueueReader(
            CreateReader(CreateJobRow(id: 9L), CreateJobRow(id: 8L)),
            command => command.Parameters["MaxCount"].Should().Be(2));
        var sut = CreateSut(connection);

        var result = await sut.GetRecentAsync(2);

        result.Should().HaveCount(2);
        result.Select(job => job.Id).Should().Equal(9, 8);
    }

    [Fact]
    public async Task GetRecentAsync_ShouldWrapConnectionFailures()
    {
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.GetRecentAsync(2);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get recent ingestion jobs");
    }

    [Fact]
    public async Task GetByManualDocumentIdAsync_ShouldReturnMatchingRows()
    {
        var manualDocumentId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(1);
        connection.EnqueueReader(
            CreateReader(CreateJobRow(manualDocumentId: manualDocumentId)),
            command => command.Parameters["ManualDocumentId"].Should().Be(manualDocumentId));
        var sut = CreateSut(connection);

        var result = await sut.GetByManualDocumentIdAsync(manualDocumentId);

        result.Should().ContainSingle().Which.ManualDocumentId.Should().Be(manualDocumentId);
    }

    [Fact]
    public async Task GetByStatusesAsync_ShouldThrowArgumentNullException_WhenStatusesAreNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.GetByStatusesAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("statuses");
    }

    [Fact]
    public async Task GetByStatusesAsync_ShouldReturnEmpty_WhenStatusesAreEmpty()
    {
        var factory = new Mock<ISqlConnectionFactory>(MockBehavior.Strict);
        var sut = new IngestionJobRepository(factory.Object, NullLogger<IngestionJobRepository>.Instance);

        var result = await sut.GetByStatusesAsync(Array.Empty<IngestionJobStatus>());

        result.Should().BeEmpty();
        factory.Verify(x => x.CreateOpenConnectionAsync(), Times.Never);
    }

    [Fact]
    public async Task GetByStatusesAsync_ShouldReturnRowsForRequestedStatuses()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(1);
        connection.EnqueueReader(
            CreateReader(CreateJobRow(status: IngestionJobStatus.Completed)),
            command =>
            {
                command.Parameters.Values.OfType<string>()
                    .Should().ContainSingle()
                    .Which.Should().Be(IngestionJobStatus.Completed.ToString());
            });
        var sut = CreateSut(connection);

        var result = await sut.GetByStatusesAsync([IngestionJobStatus.Completed]);

        result.Should().ContainSingle().Which.Status.Should().Be(IngestionJobStatus.Completed);
    }

    [Fact]
    public async Task DeleteAsync_ShouldReturnTrue_WhenADeleteOccurs()
    {
        var jobId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.Parameters["IngestionJobId"].Should().Be(jobId);
                command.CommandText.Should().Contain("DELETE FROM [dbo].[IngestionJobs]");
            });
        var sut = CreateSut(connection);

        var result = await sut.DeleteAsync(jobId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteByInputRefAsync_ShouldThrowArgumentException_WhenInputRefIsBlank()
    {
        var sut = CreateSut();

        var act = async () => await sut.DeleteByInputRefAsync(" ");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("inputRef");
    }

    [Fact]
    public async Task DeleteByInputRefAsync_ShouldReturnAffectedRowCount()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            2,
            command => command.Parameters["InputRef"].Should().Be("manuals/ducati.pdf"));
        var sut = CreateSut(connection);

        var result = await sut.DeleteByInputRefAsync("manuals/ducati.pdf");

        result.Should().Be(2);
    }

    [Fact]
    public async Task DeleteByStatusesAsync_ShouldThrowArgumentNullException_WhenStatusesAreNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.DeleteByStatusesAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("statuses");
    }

    [Fact]
    public async Task DeleteByStatusesAsync_ShouldReturnZero_WhenStatusesAreEmpty()
    {
        var factory = new Mock<ISqlConnectionFactory>(MockBehavior.Strict);
        var sut = new IngestionJobRepository(factory.Object, NullLogger<IngestionJobRepository>.Instance);

        var result = await sut.DeleteByStatusesAsync(Array.Empty<IngestionJobStatus>());

        result.Should().Be(0);
        factory.Verify(x => x.CreateOpenConnectionAsync(), Times.Never);
    }

    [Fact]
    public async Task DeleteByStatusesAsync_ShouldDeleteMatchingStatuses()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            3,
            command =>
            {
                command.Parameters.Values.OfType<string>().Should().Contain(
                    IngestionJobStatus.Completed.ToString(),
                    IngestionJobStatus.Failed.ToString());
            });
        var sut = CreateSut(connection);

        var result = await sut.DeleteByStatusesAsync([IngestionJobStatus.Completed, IngestionJobStatus.Failed]);

        result.Should().Be(3);
    }

    [Fact]
    public async Task DeleteByIdsAsync_ShouldThrowArgumentNullException_WhenIdsAreNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.DeleteByIdsAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("ingestionJobIds");
    }

    [Fact]
    public async Task DeleteByIdsAsync_ShouldReturnZero_WhenIdsAreEmpty()
    {
        var factory = new Mock<ISqlConnectionFactory>(MockBehavior.Strict);
        var sut = new IngestionJobRepository(factory.Object, NullLogger<IngestionJobRepository>.Instance);

        var result = await sut.DeleteByIdsAsync(Array.Empty<Guid>());

        result.Should().Be(0);
        factory.Verify(x => x.CreateOpenConnectionAsync(), Times.Never);
    }

    [Fact]
    public async Task DeleteByIdsAsync_ShouldDeleteMatchingIdentifiers()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            2,
            command => command.Parameters.Values.OfType<Guid>().Should().Contain(ids));
        var sut = CreateSut(connection);

        var result = await sut.DeleteByIdsAsync(ids);

        result.Should().Be(2);
    }

    [Fact]
    public async Task TryTransitionToTerminalAsync_ShouldReturnTrue_WhenUpdateSucceeds()
    {
        var jobId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.Parameters["IngestionJobId"].Should().Be(jobId);
                command.Parameters["FromStatus"].Should().Be(IngestionJobStatus.Processing.ToString());
                command.Parameters["ToStatus"].Should().Be(IngestionJobStatus.Completed.ToString());
                command.Parameters["ExpectedChunkCount"].Should().Be(12);
                command.Parameters["IndexedChunkCount"].Should().Be(11);
            });
        var sut = CreateSut(connection);

        var result = await sut.TryTransitionToTerminalAsync(
            jobId,
            IngestionJobStatus.Processing,
            IngestionJobStatus.Completed,
            12,
            11,
            null);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryTransitionToTerminalAsync_ShouldReturnFalse_WhenStatusDoesNotMatch()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(0);
        var sut = CreateSut(connection);

        var result = await sut.TryTransitionToTerminalAsync(
            Guid.NewGuid(),
            IngestionJobStatus.Processing,
            IngestionJobStatus.Completed,
            null,
            null,
            "no-op");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task TrySetDeletingAsync_ShouldReturnTrue_WhenJobIsDeletable()
    {
        var jobId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.Parameters["IngestionJobId"].Should().Be(jobId);
                command.Parameters.Values.OfType<string>().Should().Contain(
                    IngestionJobStatus.Queued.ToString(),
                    IngestionJobStatus.AwaitingMetadata.ToString(),
                    IngestionJobStatus.Completed.ToString(),
                    IngestionJobStatus.Failed.ToString(),
                    IngestionJobStatus.Cancelled.ToString(),
                    IngestionJobStatus.PartiallyCompleted.ToString());
            });
        var sut = CreateSut(connection);

        var result = await sut.TrySetDeletingAsync(jobId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task TrySetDeletingAsync_ShouldReturnFalse_WhenNoRowWasUpdated()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(0);
        var sut = CreateSut(connection);

        var result = await sut.TrySetDeletingAsync(Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateStageAsync_ShouldPersistStageProgress()
    {
        var jobId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.Parameters["IngestionJobId"].Should().Be(jobId);
                command.Parameters["Stage"].Should().Be("embedding");
                command.Parameters["ChunksProcessed"].Should().Be(8);
                command.Parameters["TotalChunks"].Should().Be(10);
                command.Parameters["FailureReason"].Should().Be("partial");
            });
        var sut = CreateSut(connection);

        await sut.UpdateStageAsync(jobId, "embedding", 8, 10, "partial");
    }

    [Fact]
    public async Task GetByDocIngestionRunIdAsync_ShouldThrowArgumentException_WhenRunIdIsBlank()
    {
        var sut = CreateSut();

        var act = async () => await sut.GetByDocIngestionRunIdAsync(" ");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("docIngestionRunId");
    }

    [Fact]
    public async Task GetByDocIngestionRunIdAsync_ShouldReturnMatchingJob()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(1);
        connection.EnqueueReader(
            CreateReader(CreateJobRow(docIngestionRunId: "run-123")),
            command => command.Parameters["DocIngestionRunId"].Should().Be("run-123"));
        var sut = CreateSut(connection);

        var result = await sut.GetByDocIngestionRunIdAsync("run-123");

        result.Should().NotBeNull();
        result!.DocIngestionRunId.Should().Be("run-123");
    }

    [Fact]
    public async Task UpdateMetadataAsync_ShouldPersistMetadataJson()
    {
        var jobId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.Parameters["IngestionJobId"].Should().Be(jobId);
                command.Parameters["MetadataJson"].Should().Be("""{"model":"Africa Twin"}""");
            });
        var sut = CreateSut(connection);

        await sut.UpdateMetadataAsync(jobId, """{"model":"Africa Twin"}""");
    }

    [Fact]
    public async Task TryTransitionFromAwaitingMetadataAsync_ShouldThrowArgumentException_WhenStageIsBlank()
    {
        var sut = CreateSut();

        var act = async () => await sut.TryTransitionFromAwaitingMetadataAsync(Guid.NewGuid(), " ");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("stage");
    }

    [Fact]
    public async Task TryTransitionFromAwaitingMetadataAsync_ShouldReturnTrue_WhenAwaitingMetadataRowWasUpdated()
    {
        var jobId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.Parameters["IngestionJobId"].Should().Be(jobId);
                command.Parameters["FromStatus"].Should().Be(IngestionJobStatus.AwaitingMetadata.ToString());
                command.Parameters["ToStatus"].Should().Be(IngestionJobStatus.Processing.ToString());
                command.Parameters["Stage"].Should().Be("chunking");
            });
        var sut = CreateSut(connection);

        var result = await sut.TryTransitionFromAwaitingMetadataAsync(jobId, "chunking");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryTransitionFromAwaitingMetadataAsync_ShouldReturnFalse_WhenNoRowWasUpdated()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(0);
        var sut = CreateSut(connection);

        var result = await sut.TryTransitionFromAwaitingMetadataAsync(Guid.NewGuid(), "chunking");

        result.Should().BeFalse();
    }

    // ---- Error path tests for methods lacking coverage ----

    [Fact]
    public async Task DeleteAsync_ShouldReturnFalse_WhenNoRowsMatched()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(0);
        var sut = CreateSut(connection);

        var result = await sut.DeleteAsync(Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ShouldWrapConnectionFailures()
    {
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.DeleteAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().StartWith("Failed to delete ingestion job ");
    }

    [Fact]
    public async Task DeleteByInputRefAsync_ShouldWrapConnectionFailures()
    {
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.DeleteByInputRefAsync("manuals/ducati.pdf");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to delete ingestion jobs by input ref");
    }

    [Fact]
    public async Task DeleteByStatusesAsync_ShouldWrapConnectionFailures()
    {
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.DeleteByStatusesAsync([IngestionJobStatus.Failed]);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to delete ingestion jobs by status");
    }

    [Fact]
    public async Task DeleteByIdsAsync_ShouldWrapConnectionFailures()
    {
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.DeleteByIdsAsync([Guid.NewGuid()]);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to delete ingestion jobs by identifier batch");
    }

    [Fact]
    public async Task TryTransitionToTerminalAsync_ShouldWrapConnectionFailures()
    {
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.TryTransitionToTerminalAsync(
            Guid.NewGuid(),
            IngestionJobStatus.Processing,
            IngestionJobStatus.Completed,
            null, null, null);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().StartWith("Failed to transition ingestion job ");
    }

    [Fact]
    public async Task TrySetDeletingAsync_ShouldWrapConnectionFailures()
    {
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.TrySetDeletingAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().StartWith("Failed to transition ingestion job ");
    }

    [Fact]
    public async Task UpdateStageAsync_ShouldWrapConnectionFailures()
    {
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.UpdateStageAsync(Guid.NewGuid(), "embedding", null, null, null);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().StartWith("Failed to update stage for ingestion job ");
    }

    [Fact]
    public async Task GetByDocIngestionRunIdAsync_ShouldWrapConnectionFailures()
    {
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.GetByDocIngestionRunIdAsync("run-123");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().StartWith("Failed to get ingestion job by doc ingestion run id ");
    }

    [Fact]
    public async Task UpdateMetadataAsync_ShouldWrapConnectionFailures()
    {
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.UpdateMetadataAsync(Guid.NewGuid(), "{}");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().StartWith("Failed to update metadata for ingestion job ");
    }

    [Fact]
    public async Task TryTransitionFromAwaitingMetadataAsync_ShouldWrapConnectionFailures()
    {
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.TryTransitionFromAwaitingMetadataAsync(Guid.NewGuid(), "chunking");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().StartWith("Failed to transition ingestion job ");
    }

    [Fact]
    public async Task GetByManualDocumentIdAsync_ShouldWrapConnectionFailures()
    {
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.GetByManualDocumentIdAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().StartWith("Failed to get ingestion jobs for manual document ");
    }

    [Fact]
    public async Task GetByStatusesAsync_ShouldWrapConnectionFailures()
    {
        var sut = CreateThrowingSut(new InvalidOperationException("sql down"));

        var act = async () => await sut.GetByStatusesAsync([IngestionJobStatus.Completed]);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get ingestion jobs by status");
    }

    private static IngestionJobRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new IngestionJobRepository(factory.Object, NullLogger<IngestionJobRepository>.Instance);
    }

    private static IngestionJobRepository CreateThrowingSut(Exception exception)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory.Setup(x => x.CreateOpenConnectionAsync()).ThrowsAsync(exception);
        return new IngestionJobRepository(factory.Object, NullLogger<IngestionJobRepository>.Instance);
    }

    private static IngestionJob CreateJob(
        IngestionJobStatus status = IngestionJobStatus.Queued,
        DateTimeOffset? stageSetAtUtc = null,
        int? pagesCapturedViewableCount = null) =>
        IngestionJob.Rehydrate(
            id: 0,
            ingestionJobId: Guid.NewGuid(),
            createdAtUtc: new DateTimeOffset(2026, 7, 10, 13, 0, 0, TimeSpan.Zero),
            startedAtUtc: null,
            completedAtUtc: null,
            createdBySubject: null,
            status: status,
            failureReason: null,
            errorsJson: null,
            errorMessage: null,
            inputType: IngestionJobType.PDFManual,
            inputRef: "manuals/honda-vfr.pdf",
            sourceFileName: "honda-vfr.pdf",
            computeProvider: "AdminLocalProcessor",
            docIngestionRunId: null,
            manualDocumentId: null,
            totalPages: null,
            pagesCapturedViewableCount: pagesCapturedViewableCount,
            pagesWithSearchableTextCount: null,
            pagesWithOcrTextCount: null,
            pagesWithNativeTextCount: null,
            missingPagesJson: null,
            metricsJson: null,
            expectedChunkCount: null,
            indexedChunkCount: null,
            currentStage: "queued",
            stageSetAtUtc: stageSetAtUtc,
            metadataJson: null);

    private static Dictionary<string, object?> CreateJobRow(
        long id = 7L,
        Guid? ingestionJobId = null,
        IngestionJobStatus status = IngestionJobStatus.Queued,
        IngestionJobType inputType = IngestionJobType.PDFManual,
        string inputRef = "manuals/test.pdf",
        string? currentStage = "queued",
        string? metadataJson = null,
        Guid? manualDocumentId = null,
        string? docIngestionRunId = null)
    {
        var jobId = ingestionJobId ?? Guid.NewGuid();
        return new Dictionary<string, object?>
        {
            ["Id"] = id,
            ["IngestionJobId"] = jobId,
            ["Status"] = (int)status,
            ["InputType"] = (int)inputType,
            ["InputRef"] = inputRef,
            ["CurrentStage"] = currentStage,
            ["MetadataJson"] = metadataJson,
            ["ManualDocumentId"] = manualDocumentId,
            ["DocIngestionRunId"] = docIngestionRunId,
            ["ComputeProvider"] = "AdminLocalProcessor"
        };
    }

    private static DbDataReader CreateReader(params IReadOnlyDictionary<string, object?>[] rows)
    {
        var table = new DataTable();
        var columnNames = rows.SelectMany(static row => row.Keys).Distinct(StringComparer.Ordinal).ToList();
        foreach (var columnName in columnNames)
        {
            table.Columns.Add(columnName, typeof(object));
        }

        foreach (var row in rows)
        {
            var dataRow = table.NewRow();
            foreach (var columnName in columnNames)
            {
                dataRow[columnName] = row.TryGetValue(columnName, out var value) ? value ?? DBNull.Value : DBNull.Value;
            }

            table.Rows.Add(dataRow);
        }

        return table.CreateDataReader();
    }

    private sealed class FakeDbConnection : DbConnection
    {
        private readonly Queue<CommandPlan> _plans = new();

        public List<ExecutedCommand> ExecutedCommands { get; } = [];

        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "Fake";
        public override string DataSource => "Fake";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Open;

        public void EnqueueScalar(object? result, Action<ExecutedCommand>? assert = null)
        {
            _plans.Enqueue(new CommandPlan(CommandKind.Scalar, () => result, assert));
        }

        public void EnqueueNonQuery(int affectedRows, Action<ExecutedCommand>? assert = null)
        {
            _plans.Enqueue(new CommandPlan(CommandKind.NonQuery, () => affectedRows, assert));
        }

        public void EnqueueReader(DbDataReader reader, Action<ExecutedCommand>? assert = null)
        {
            _plans.Enqueue(new CommandPlan(CommandKind.Reader, () => reader, assert));
        }

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotSupportedException();
        }

        protected override DbCommand CreateDbCommand() => new FakeDbCommand(this);

        internal object? Execute(CommandKind kind, FakeDbCommand command)
        {
            _plans.Should().NotBeEmpty("every repository call in these tests should have a planned DB response");
            var plan = _plans.Dequeue();
            plan.Kind.Should().Be(kind);

            var executed = new ExecutedCommand(command.CommandText, command.GetParameters());
            ExecutedCommands.Add(executed);
            plan.Assert?.Invoke(executed);
            return plan.ResultFactory();
        }
    }

    private sealed class FakeDbCommand : DbCommand
    {
        private readonly FakeDbConnection _connection;
        private readonly FakeDbParameterCollection _parameters = new();

        public FakeDbCommand(FakeDbConnection connection)
        {
            _connection = connection;
        }

        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection DbConnection
        {
            get => _connection;
            set => throw new NotSupportedException();
        }

        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }
        public override int ExecuteNonQuery() => (int)(_connection.Execute(CommandKind.NonQuery, this) ?? 0);
        public override object? ExecuteScalar() => _connection.Execute(CommandKind.Scalar, this);
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new FakeDbParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            (DbDataReader)(_connection.Execute(CommandKind.Reader, this)
                ?? throw new InvalidOperationException("Reader result was null."));

        internal Dictionary<string, object?> GetParameters() =>
            _parameters
                .Cast<FakeDbParameter>()
                .ToDictionary(parameter => parameter.ParameterName, parameter => parameter.Value, StringComparer.Ordinal);
    }

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = [];

        public override int Count => _parameters.Count;
        public override object SyncRoot => ((ICollection)_parameters).SyncRoot;
        public override int Add(object value)
        {
            _parameters.Add((DbParameter)value);
            return _parameters.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value!);
            }
        }

        public override void Clear() => _parameters.Clear();
        public override bool Contains(object value) => _parameters.Contains((DbParameter)value);
        public override bool Contains(string value) => _parameters.Any(parameter => parameter.ParameterName == value);
        public override void CopyTo(Array array, int index) => ((ICollection)_parameters).CopyTo(array, index);
        public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();
        public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => _parameters.FindIndex(parameter => parameter.ParameterName == parameterName);
        public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _parameters.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _parameters.RemoveAt(index);

        public override void RemoveAt(string parameterName)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _parameters.RemoveAt(index);
            }
        }

        protected override DbParameter GetParameter(int index) => _parameters[index];
        protected override DbParameter GetParameter(string parameterName) => _parameters[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _parameters[index] = value;
            }
            else
            {
                _parameters.Add(value);
            }
        }
    }

    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        public override string ParameterName { get; set; } = string.Empty;
        public override string SourceColumn { get; set; } = string.Empty;
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed record CommandPlan(
        CommandKind Kind,
        Func<object?> ResultFactory,
        Action<ExecutedCommand>? Assert);

    private sealed record ExecutedCommand(
        string CommandText,
        IReadOnlyDictionary<string, object?> Parameters);

    private enum CommandKind
    {
        Scalar,
        NonQuery,
        Reader
    }
}
