namespace RepoChangesSearcher.Core.Models
{
    public class ProcessedFilesModel
    {
        public string FileName { get; set; }
        public string ProjectPath { get; set; }
        public string RepoPath { get; set; }
        public bool SuccessfullyProcessed { get; set; }
        public string ErrorMessage { get; set; }
    }
}
