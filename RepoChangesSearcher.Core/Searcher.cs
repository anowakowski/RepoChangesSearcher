using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using RepoChangesSearcher.Core.Models;
using Serilog;

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

            Log.Information($"Start Files for configuration: projectPath {projectsPath}, branchName: {branchToSearch}, author: {authorEmail}");

            _direcotryPaths.AddRange(Directory.GetDirectories(projectsPath).Select(x => new DirectoryInfo(x).FullName));
            CreateDirectoryToMoveFiles(projectsPath);

            Log.Information($"Found: {_direcotryPaths.Count()} projects to search in {projectsPath}");

            var stopLoop = false;
            _direcotryPaths.ForEach(path =>
            {
                if (stopLoop) return;
                InitRepo(path);
                
                if (_repo != null)
                {
                    Log.Information($"Configure project repo: {path}");
                    if (_repo.Branches.Any(x => x.FriendlyName == branchToSearch))
                    {
                        var branch = _repo.Branches.FirstOrDefault(x => x.FriendlyName == branchToSearch);
                        if (!branch.IsCurrentRepositoryHead)
                        {
                            Log.Error($"Your branch for project repo: {path} is not set as CurrentRepositoryHead branch: {branchToSearch}, you need to chenge this before start search process proccess");
                            stopLoop = true;
                            return;
                        }

                        Log.Information($"Search for project repo: {path} in progress...");
                        var searchedBranch = _repo.Branches.Single(x => x.FriendlyName == branchToSearch);
                        var comits = searchedBranch.Commits
                                        .Where(x =>
                                           x.Author.When.Date >= dateFrom && x.Author.When.Date <= dateTo &&
                                           x.Author.Email.Equals(authorEmail))
                                        .ToList();

                        SetChangesFiles(comits);
                        RemoveDuplicates();
                        ProcessFiles(projectsPath, path);

                        Log.Information($"End process for project repo: {path}");
                        Log.Information($"Copy {_allProcessedFiles.Count(x => x.SuccessfullyProcessed)} files for project repo: {path}");

                        if (_allProcessedFiles.Any(x => !x.SuccessfullyProcessed))
                        {
                            Log.Warning($"Some file not proccessed for project repo:{path}, not processed files: {_allProcessedFiles.Where(x => x.RepoPath == path).Count(x => !x.SuccessfullyProcessed)}");
                        }

                        _repo = null;
                    }
                }
            });
            
            Log.Information($"Finished search and copy process, copy: {_allProcessedFiles.Count(x => x.SuccessfullyProcessed)}, and {_allProcessedFiles.Count(x => !x.SuccessfullyProcessed)} not processed");
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

                        if (!File.Exists(destFilePath))
                        {
                            File.Copy(file, destFilePath);

                            _allProcessedFiles.Add(new ProcessedFilesModel { FileName = changedFile, ProjectPath = repoPath, SuccessfullyProcessed = true, RepoPath = repoPath });
                        }
                    }
                    else
                    {
                        _allProcessedFiles.Add(new ProcessedFilesModel { FileName = changedFile, ProjectPath = repoPath, SuccessfullyProcessed = false, RepoPath = repoPath });
                    }
                }
                catch(Exception ex)
                {
                    var message = ex.ToString();
                    Log.Error(ex, message);
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
                Log.Error($"Can't create new directory for searched files, error message: {ex.Message}");
            }
        }

        private void RemoveDuplicates()
        {
            _changedFiles = _changedFiles.Distinct().ToList();
        }

        private void SetChangesFiles(List<Commit> commits)
        {
            try
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
            catch(Exception ex)
            {
                Log.Error($"Error during searching changes files in commits, error message: {ex.Message}");
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