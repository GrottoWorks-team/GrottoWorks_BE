namespace TaskExecution.Application.Tasks;

public sealed class TaskConcurrencyException(Exception innerException)
    : Exception("The task was updated by another request.", innerException);
