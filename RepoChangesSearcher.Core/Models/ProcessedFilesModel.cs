namespace RepoChangesSearcher.Core.Models
{
    public class ProcessedFilesModel
    {
        public string FileName { get; set; }
        public string ProjectPath { get; set; }
        public bool SuccessfullyProcessed { get; set; }
    }
}
