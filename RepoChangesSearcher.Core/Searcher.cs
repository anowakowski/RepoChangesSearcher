using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using RepoChangesSearcher.Core.Models;
using System.Diagnostics.Metrics;
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
            var authorEmail = configuration.authorEmail;

            _direcotryPaths.AddRange(Directory.GetDirectories(projectsPath).Select(x => new DirectoryInfo(x).FullName));
            CreateDirectoryToMoveFiles(projectsPath);

            _direcotryPaths.Where(x => x.Contains("RAS")).ToList().ForEach(path =>
            {
                InitRepo(path);

                if (_repo != null)
                {
                    if (_repo.Branches.Any(x => x.FriendlyName == branchToSearch))
                    {
                        var searchedBranch = _repo.Branches.Single(x => x.FriendlyName == branchToSearch);
                        var comits = searchedBranch.Commits
                                        .Where(x =>
                                           x.Author.When.Date >= dateFrom && x.Author.When.Date <= dateTo &&
                                           x.Author.Email.Equals(authorEmail))
                                        .ToList();

                        SetChangesFiles(comits);
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
                try
                {
                    string file = Directory.GetFiles(repoPath, "*.*", SearchOption.AllDirectories).FirstOrDefault(x => x.Contains(changedFile));

                    if (!string.IsNullOrEmpty(file))
                    {
                        var destFilePath = Path.Combine(GetOutputCatalogPath(projectsPath), changedFile);
                        File.Copy(file, destFilePath);

                        _allProcessedFiles.Add(new ProcessedFilesModel { FileName = changedFile, ProjectPath = repoPath, SuccessfullyProcessed = true });
                    }
                    else
                    {
                        _allProcessedFiles.Add(new ProcessedFilesModel { FileName = changedFile, ProjectPath = repoPath, SuccessfullyProcessed = false });
                    }
                }
                catch(Exception ex)
                {
                    var message = ex.ToString(); 
                }
            });
        }

        private string GetOutputCatalogPath(string projectsPath) => Path.Combine(projectsPath, "ChangedFilesFromRepository");

        private void CreateDirectoryToMoveFiles(string projectsPath)
        {
            try
            {
                bool exists = Directory.Exists(GetOutputCatalogPath(projectsPath));
                if (!exists) Directory.CreateDirectory(GetOutputCatalogPath(projectsPath));
            }
            catch(Exception ex)
            {

            }
        }

        private void RemoveDuplicates()
        {
            _changedFiles = _changedFiles.Distinct().ToList();
        }

        private void SetChangesFiles(List<Commit> commits)
        {
            if (commits.Any()) 
            {
                commits.ForEach(commit =>
                {
                    var tree = commit.Tree;
                    var treeParent = commit.Parents.FirstOrDefault().Tree;

                    var patch = _repo.Diff.Compare<Patch>(treeParent, tree).ToList();

                    if (patch != null)
                    {
                        _changedFiles.AddRange(patch.Where(x => x.Status == ChangeKind.Modified).Select(x => Path.GetFileName(x.Path)));
                    }
                });
            
            }
        }

        private void SetChangedFilesOld(List<Commit> comits)
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

        private (string projectsPath, string branchToSearch, string dateFrom, string dateTo, string authorEmail) GetConfiguration ()
        {
            var section = _configuration.GetSection("SearcherInfo");

            if (section == null) throw new InvalidOperationException("section SearcherInfo dosen't exists");

            var projectsPath = section.GetSection("ProjectsPath").Value;
            var searchedBranch = section.GetSection("SearchedBranch").Value;
            var dateFrom = section.GetSection("dateFrom").Value;
            var dateTo = section.GetSection("dateTo").Value;
            var authorEmail = section.GetSection("AuthorEmail").Value;

            return (projectsPath, searchedBranch, dateFrom, dateTo, authorEmail);
        }


    }
}