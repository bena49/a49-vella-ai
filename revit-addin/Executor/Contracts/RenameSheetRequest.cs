namespace A49AIRevitAssistant.Executor.Contracts
{
    public class RenameSheetRequest
    {
        public string target { get; set; }      // sheet number
        public string new_name { get; set; }    // new sheet name
    }
}
