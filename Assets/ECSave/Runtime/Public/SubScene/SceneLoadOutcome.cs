namespace ECSave
{
    public readonly struct SceneLoadOutcome
    {
        public bool Success { get; }
        public string FailureReason { get; }

        public SceneLoadOutcome(bool success, string failureReason = null)
        {
            Success = success;
            FailureReason = failureReason;
        }

        public static SceneLoadOutcome Ok() => new(true, null);
        public static SceneLoadOutcome Fail(string reason) => new(false, reason);
    }
}
