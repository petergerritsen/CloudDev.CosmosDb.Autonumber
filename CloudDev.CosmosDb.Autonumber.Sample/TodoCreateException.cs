namespace CloudDev.CosmosDb.Autonumber
{
    using System;

    public class TodoCreateException : Exception
    {
        public TodoCreateException(TodoCreateErrorType errorType)
        {
            ErrorType = errorType;
        }

        public TodoCreateErrorType ErrorType { get; }
    }

    public enum TodoCreateErrorType
    {
        AutonumberError,
        TodoItemError
    }
}