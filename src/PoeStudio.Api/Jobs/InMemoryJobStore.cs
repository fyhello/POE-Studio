using System.Collections.Concurrent;
using PoeStudio.Contracts;

namespace PoeStudio.Api.Jobs;

public sealed class InMemoryJobStore
{
    private readonly ConcurrentDictionary<string, MutableJob> jobs = new(StringComparer.OrdinalIgnoreCase);

    public JobSnapshotDto Create(string kind, string message)
    {
        var now = DateTimeOffset.UtcNow;
        var job = new MutableJob(
            id: Guid.NewGuid().ToString("N"),
            kind,
            status: JobStatus.Queued,
            progressPercent: 0,
            message,
            createdAt: now,
            updatedAt: now,
            errorCode: null,
            errorMessage: null,
            resultJson: null);
        jobs[job.Id] = job;
        return job.ToSnapshot();
    }

    public JobSnapshotDto? Get(string id)
    {
        return jobs.TryGetValue(id, out var job) ? job.ToSnapshot() : null;
    }

    public JobSnapshotDto Update(string id, JobStatus status, int progressPercent, string message)
    {
        var job = Require(id);
        lock (job)
        {
            job.Status = status;
            job.ProgressPercent = Math.Clamp(progressPercent, 0, 100);
            job.Message = message;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            return job.ToSnapshot();
        }
    }

    public JobSnapshotDto Succeed(string id, string message, string resultJson)
    {
        var job = Require(id);
        lock (job)
        {
            job.Status = JobStatus.Succeeded;
            job.ProgressPercent = 100;
            job.Message = message;
            job.ResultJson = resultJson;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            return job.ToSnapshot();
        }
    }

    public JobSnapshotDto Fail(string id, string errorCode, string errorMessage)
    {
        var job = Require(id);
        lock (job)
        {
            job.Status = JobStatus.Failed;
            job.ErrorCode = errorCode;
            job.ErrorMessage = errorMessage;
            job.Message = errorMessage;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            return job.ToSnapshot();
        }
    }

    private MutableJob Require(string id)
    {
        if (!jobs.TryGetValue(id, out var job))
        {
            throw new KeyNotFoundException($"Job not found: {id}");
        }

        return job;
    }

    private sealed class MutableJob
    {
        public MutableJob(
            string id,
            string kind,
            JobStatus status,
            int progressPercent,
            string message,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt,
            string? errorCode,
            string? errorMessage,
            string? resultJson)
        {
            Id = id;
            Kind = kind;
            Status = status;
            ProgressPercent = progressPercent;
            Message = message;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            ResultJson = resultJson;
        }

        public string Id { get; }

        public string Kind { get; }

        public JobStatus Status { get; set; }

        public int ProgressPercent { get; set; }

        public string Message { get; set; }

        public DateTimeOffset CreatedAt { get; }

        public DateTimeOffset UpdatedAt { get; set; }

        public string? ErrorCode { get; set; }

        public string? ErrorMessage { get; set; }

        public string? ResultJson { get; set; }

        public JobSnapshotDto ToSnapshot()
        {
            return new JobSnapshotDto(
                Id,
                Kind,
                Status,
                ProgressPercent,
                Message,
                CreatedAt,
                UpdatedAt,
                ErrorCode,
                ErrorMessage,
                ResultJson);
        }
    }
}
