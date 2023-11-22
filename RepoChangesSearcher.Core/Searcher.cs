using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using RepoChangesSearcher.Core.Models;
using System.IO;

namespace RepoChangesSearcher.Core
{
    public class Searcher : ISearcher, IDisposable
    {
        private Repository _repo;
        private bool _disposed;
        private readonly IConfiguration _configuration;
        private List<string> _changedFiles = new List<string>();
        private List<string> _direcotryPaths = new List<string>();
        private List<ProcessedFilesModel> _allProcessedFiles = new List<ProcessedFilesModel>();

        public Searcher(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Search()
        {
            var configuration = GetConfiguration();

            var projectsPath = configuration.projectsPath;
            var branchToSearch = configuration.branchToSearch;
            var dateFrom = DateTime.Parse(configuration.dateFrom);
            var dateTo = DateTime.Parse(configuration.dateTo);
            var commiterEmail = configuration.commiterEmail;

            _direcotryPaths.AddRange(Directory.GetDirectories(projectsPath).Select(x => new DirectoryInfo(x).FullName));
            CreateDirectoryToMoveFiles(projectsPath);

            _direcotryPaths.ForEach(path =>
            {
                InitRepo(path);

                if (_repo != null)
                {
                    if (_repo.Branches.Any(x => x.FriendlyName == branchToSearch))
                    {
                        var searchedBranch = _repo.Branches.Single(x => x.FriendlyName == branchToSearch);
                        var comits = searchedBranch.Commits
                                        .Where(x =>
                                           x.Committer.When.Date >= dateFrom && x.Committer.When.Date <= dateTo &&
                                           x.Committer.Email.Equals(commiterEmail))
                                        .ToList();

                        SetChangedFiles(comits);
                        RemoveDuplicates();
                        ProcessFiles(projectsPath, path);
                        
                        _repo = null;
                    }
                }
            });
        }
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _repo.Dispose();
            }
        }

        private void ProcessFiles(string projectsPath, string repoPath)
        {
            _changedFiles.ForEach(changedFile =>
            {
                string file = Directory.GetFiles(repoPath, "*.*", SearchOption.AllDirectories).FirstOrDefault(x => x.Contains(changedFile));
                //Copy functionality

                _allProcessedFiles.Add(new ProcessedFilesModel { FileName = changedFile, ProjectPath = repoPath, SuccessfullyProcessed = true});
            });
        }

        private string GetPathToCreateDirectory(string projectsPath) => Path.Combine(projectsPath, "ChangedFilesFromRepository");

        private void CreateDirectoryToMoveFiles(string projectsPath)
        {
            try
            {
                bool exists = Directory.Exists(GetPathToCreateDirectory(projectsPath));
                if (!exists) Directory.CreateDirectory(GetPathToCreateDirectory(projectsPath));
            }
            catch(Exception ex)
            {

            }
        }

        private void RemoveDuplicates()
        {
            _changedFiles = _changedFiles.Distinct().ToList();
        }

        private void SetChangedFiles(List<Commit> comits)
        {
            if (comits.Any())
            {
                comits.ForEach(comit =>
                {
                    var trees = comit.Tree.Where(x => x.TargetType == TreeEntryTargetType.Tree).ToList();
                    trees.ForEach(tree =>
                    {
                        var targetTrees = (tree.Target as Tree).ToList();

                        targetTrees.ForEach(targetTree => {

                            if (targetTree.TargetType == TreeEntryTargetType.Blob)
                            {
                                _changedFiles.Add(targetTree.Name);
                            }
                            else
                            {
                                var targetTreeProj = targetTree.Target as Tree;

                                var blobFiles = targetTreeProj.Where(x => x.TargetType == TreeEntryTargetType.Blob).ToList();

                                if (blobFiles.Any())
                                {
                                    _changedFiles.AddRange(blobFiles.Select(x => x.Name));
                                }
                            }
                        });

                    });
                });
            }
        }

        private void InitRepo(string repoPath)
        {
            if (Repository.IsValid(repoPath))
            {
                _repo = new Repository(repoPath);
            }
        }

        private (string projectsPath, string branchToSearch, string dateFrom, string dateTo, string commiterEmail) GetConfiguration ()
        {
            var section = _configuration.GetSection("SearcherInfo");

            if (section == null) throw new InvalidOperationException("section SearcherInfo dosen't exists");

            var projectsPath = section.GetSection("ProjectsPath").Value;
            var searchedBranch = section.GetSection("SearchedBranch").Value;
            var dateFrom = section.GetSection("dateFrom").Value;
            var dateTo = section.GetSection("dateTo").Value;
            var commiterEmail = section.GetSection("CommiterEmail").Value;

            return (projectsPath, searchedBranch, dateFrom, dateTo, commiterEmail);
        }


    }
}