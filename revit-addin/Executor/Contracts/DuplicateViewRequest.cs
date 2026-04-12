namespace A49AIRevitAssistant.Executor.Contracts
{
    public class DuplicateViewRequest
    {
        public string target { get; set; }      // view name
        public string mode { get; set; }        // "with detailing" or ""
    }
}
