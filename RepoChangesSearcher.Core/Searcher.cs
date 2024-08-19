using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using RepoChangesSearcher.Core.Models;
using Serilog;
using System.Text;

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

        private const string DefaultOutputCatalogueBaseName = "ChangedFilesFromRepository";
        private const string DefaultPathForRasAPI2Proj = "C:\\Projects\\RASAPI2";
        private const string CorrectSufixForRasAPI2ProjectPath = "\\rasapi";

        private string defaultOutputCatalogueName = string.Empty;
        private bool shouldUseDefaultOutputCatalogue = false;

        public Searcher(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Search()
        {
            var configuration = GetConfiguration();

            if (!CheckConfigurationValues(configuration) || !ValidateProjectPath(configuration.projectsPath)) return;
            if (!SetupDestinationPath(configuration.projectsPath, configuration.destinationOutputPath)) return;

            var projectsPath = configuration.projectsPath;
            var branchToSearch = configuration.branchToSearch;
            var dateFrom = DateTime.Parse(configuration.dateFrom);
            var dateTo = DateTime.Parse(configuration.dateTo);
            var authorEmail = configuration.authorEmail;
            var destinationOutputPath = configuration.destinationOutputPath;

            _direcotryPaths.AddRange(Directory.GetDirectories(projectsPath).Select(x => new DirectoryInfo(x).FullName).Where(x => x != GetOutputCatalogPath(projectsPath, defaultOutputCatalogueName)));

            if (!_direcotryPaths.Any())
            {
                Log.Error($"Not found any projects in configuret projectsPath: {projectsPath}");
                return;
            }

            SetCorrectValForRASAPI2Project();

            Log.Information($"Start search Files from repo for configuration: projectPath {projectsPath}, branchName: {branchToSearch}, author: {authorEmail}");

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

                        if (!comits.Any())
                        {
                            Log.Warning($"Not found any commits for branch: {searchedBranch} for project repo: {path}, search configuration with the specified date rane: dateFrom: {dateFrom} and dateTo: {dateTo}");
                            Log.Information($"End process for project repo: {path}");
                            return;
                        }

                        SetChangesFiles(comits);
                        RemoveDuplicates();
                        ProcessFiles(projectsPath, path, destinationOutputPath);

                        Log.Information($"End process for project repo: {path}");
                        Log.Information($"Copy {_allProcessedFiles.Count(x => x.SuccessfullyProcessed && x.RepoPath == path)} files for project repo: {path}");

                        if (_allProcessedFiles.Any(x => !x.SuccessfullyProcessed && x.RepoPath == path))
                        {
                            Log.Warning($"Some file not proccessed for project repo:{path}, not processed files: {_allProcessedFiles.Where(x => x.RepoPath == path && !x.SuccessfullyProcessed).Count()}");
                        }

                        _repo = null;
                        _changedFiles.Clear();
                    }
                }
                else
                {
                    Log.Warning($"project repository {path} is not GIT repository");
                }
            });

            Log.Information($"Finished search and copy process, copy: {_allProcessedFiles.Count(x => x.SuccessfullyProcessed)}, and {_allProcessedFiles.Count(x => !x.SuccessfullyProcessed)} not processed");

            if (_allProcessedFiles.Any(x => !x.SuccessfullyProcessed))
            {
                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine("Not processed files: ");
                _allProcessedFiles
                    .Where(x => !x.SuccessfullyProcessed)
                    .ToList()
                    .ForEach(file =>
                    {
                        sb.AppendLine($"not processed file => name: {file.FileName}, repoPath: {file.RepoPath}, message: {file.ErrorMessage}");
                    });

                Log.Warning(sb.ToString());
            }

            var outputCatalogue = shouldUseDefaultOutputCatalogue ? GetOutputCatalogPath(projectsPath, defaultOutputCatalogueName) : destinationOutputPath;
            Log.Information($"Find the copied files in the location: {outputCatalogue}");
        }

        private void SetCorrectValForRASAPI2Project()
        {
            if (_direcotryPaths.Any(x => x.Equals(DefaultPathForRasAPI2Proj)))
            {
                var val = _direcotryPaths.Single(x => x.Equals(DefaultPathForRasAPI2Proj));

                var index = _direcotryPaths.IndexOf(val);

                _direcotryPaths.RemoveAt(index);

                val = string.Concat(val, CorrectSufixForRasAPI2ProjectPath);

                _direcotryPaths.Insert(index, val);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                if (_repo != null)
                {
                    _repo.Dispose();
                }
            }
        }

        private bool SetupDestinationPath(string projectsPath, string destinationOutputPath)
        {
            try
            {
                if (string.IsNullOrEmpty(destinationOutputPath))
                {
                    CreateDirectoryToMoveFiles(projectsPath);

                    if (!CheckIfDirectoryIsEmpty(GetOutputCatalogPath(projectsPath, defaultOutputCatalogueName)))
                    {
                        Log.Error($"Your destination output path: {GetOutputCatalogPath(projectsPath, defaultOutputCatalogueName)} is not empty");
                        return false;
                    }

                    Log.Information($"Set destination catalogue as: {GetOutputCatalogPath(projectsPath, defaultOutputCatalogueName)}");
                    
                    shouldUseDefaultOutputCatalogue = true;
                    
                    return true;
                }
                else if (!Directory.Exists(destinationOutputPath))
                {
                    Log.Error($"Your destiantion output path: {destinationOutputPath} not exist check your appsettings.json configuration");
                    return false;
                }

                if (!CheckIfDirectoryIsEmpty(destinationOutputPath))
                {
                    Log.Error($"Your destination output path: {destinationOutputPath} is not empty");
                    return false;
                }

                Log.Information($"Set destination catalogue as :{destinationOutputPath}");
                return true;
            }
            catch(Exception ex)
            {
                Log.Error($"Error during SetupDestinationPath, error message: {ex.Message}");
                return false;
            }

        }

        private bool CheckIfDirectoryIsEmpty(string directory)
        {
            if(!Directory.EnumerateFiles(directory,"*", SearchOption.AllDirectories).Any())
            {
                return true;
            }
            return false;
        }

        private bool ValidateProjectPath(string projectPath)
        {
            if (Directory.Exists(projectPath))
            {
                return true;
            }

            Log.Error($"Yourt projectPath: {projectPath} configured on appsettings.json not exists");
            return false;   
        }

        private bool CheckConfigurationValues((string projectsPath, string branchToSearch, string dateFrom, string dateTo, string authorEmail, string destinationOutputPath) configuration)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Some errors appears with configuration, check appsetting.json file");
            sb.AppendLine("Erros: ");
            var result = true;
            if (string.IsNullOrEmpty(configuration.projectsPath))
            {
                sb.AppendLine("configuration field projectsPath can't be empty");
                result = false;
            }
            if (string.IsNullOrEmpty(configuration.branchToSearch))
            {
                sb.AppendLine("configuration field branchToSearch can't be empty");
                result = false;
            }
            if (string.IsNullOrEmpty(configuration.dateFrom))
            {
                sb.AppendLine("configuration field dateFrom can't be empty");
                result = false;
            }
            else if (!string.IsNullOrEmpty(configuration.dateFrom) && !ValidateDateFormat(configuration.dateFrom))
            {
                sb.AppendLine($"field dateFrom has incorrect formt: {configuration.dateFrom}, it should be date");
                result = false;
            }
            if (string.IsNullOrEmpty(configuration.dateTo))
            {
                sb.AppendLine("configuration field dateTo can't be empty");
                result = false;
            }
            else if (!string.IsNullOrEmpty(configuration.dateTo) && !ValidateDateFormat(configuration.dateTo))
            {
                sb.AppendLine($"field dateTo has incorrect formt: {configuration.dateTo}, it should be date");
                result = false;
            }
            if (string.IsNullOrEmpty(configuration.authorEmail))
            {
                sb.AppendLine("configuration field authorEmail can't be empty");
                result = false;
            }

            if (!result)
            {
                Log.Error(sb.ToString());
            }

            return result;
        }

        private bool ValidateDateFormat(string date)
        {
            return DateTime.TryParse(date, out var dateOut);
        }

        private void ProcessFiles(string projectsPath, string repoPath, string destinationOutputPath)
        {
            _changedFiles.ForEach(changedFile =>
            {
                try
                {
                    string file = Directory.GetFiles(repoPath, "*.*", SearchOption.AllDirectories).FirstOrDefault(x => x.Contains(changedFile));

                    if (!string.IsNullOrEmpty(file))
                    {
                        string destFilePath;
                        
                        if (shouldUseDefaultOutputCatalogue)
                        {
                            destFilePath = Path.Combine(GetOutputCatalogPath(projectsPath, defaultOutputCatalogueName), changedFile);
                        }
                        else
                        {
                            destFilePath = Path.Combine(destinationOutputPath, changedFile);
                        }                        

                        if (!File.Exists(destFilePath))
                        {
                            File.Copy(file, destFilePath);

                            _allProcessedFiles.Add(new ProcessedFilesModel { FileName = changedFile, ProjectPath = repoPath, SuccessfullyProcessed = true, RepoPath = repoPath });
                        }
                        else
                        {
                            _allProcessedFiles.Add(new ProcessedFilesModel { FileName = changedFile, ProjectPath = repoPath, SuccessfullyProcessed = false, RepoPath = repoPath, ErrorMessage = $"File {changedFile} arledy exist in destination path" });
                        }
                    }
                    else
                    {
                        _allProcessedFiles.Add(new ProcessedFilesModel { FileName = changedFile, ProjectPath = repoPath, SuccessfullyProcessed = false, RepoPath = repoPath, ErrorMessage = $"File {changedFile} not exist in repoPath: {repoPath}" });
                    }
                }
                catch(Exception ex)
                {
                    var message = ex.ToString();
                    Log.Error(ex, message);
                }
            });
        }

        private string GetOutputCatalogPath(string projectsPath, string outputCatalogueName) => Path.Combine(projectsPath, outputCatalogueName);

        private void PrepareDefaultOutputCatalogueName()
        {
            string dateTime = DateTime.Now.ToString("yyyyMMdd");
            defaultOutputCatalogueName = string.Concat(DefaultOutputCatalogueBaseName, "_", dateTime);
        }

        private void CreateDirectoryToMoveFiles(string projectsPath)
        {
            try
            {
                PrepareDefaultOutputCatalogueName();
                bool exists = Directory.Exists(GetOutputCatalogPath(projectsPath, defaultOutputCatalogueName));
                if (!exists)
                {
                    Directory.CreateDirectory(GetOutputCatalogPath(projectsPath, defaultOutputCatalogueName));
                    Log.Information($"Created new direcotry on path: {GetOutputCatalogPath(projectsPath, defaultOutputCatalogueName)} ");
                }
            }
            catch(Exception ex)
            {
                Log.Error($"Can't create new directory for searched files, error message: {ex.Message}");
            }
        }

        private void RemoveDuplicates()
        {
            if (_changedFiles.Any())
            {
                _changedFiles = _changedFiles.Distinct().ToList();
            }
        }

        private void SetChangesFiles(List<Commit> commits)
        {
            try
            {
                if (commits.Any())
                {
                    commits.ForEach(commit =>
                    {
                        Tree tree = commit.Tree;
                        Tree treeParent = null;

                        var parentsComit = commit.Parents.FirstOrDefault();                        
                       
                        if (parentsComit != null)
                        {
                            treeParent = commit.Parents.FirstOrDefault().Tree;

                            var patch = PrepareCompareForPatchEntryChanges(treeParent, tree);
                            AddToChangedFiles(patch);
                        }
                        else
                        {
                            var patch = PrepareCompareForPatchEntryChanges(treeParent, tree);
                            AddToChangedFiles(patch);
                        }
                    });
                }
            }
            catch(Exception ex)
            {
                Log.Error($"Error during searching changes files in commits, error message: {ex.Message}");
            }
        }

        private List<PatchEntryChanges> PrepareCompareForPatchEntryChanges(Tree treeParent, Tree tree) => _repo.Diff.Compare<Patch>(treeParent, tree).ToList();

        private void AddToChangedFiles(List<PatchEntryChanges> patch)
        {
            if (patch != null)
            {
                _changedFiles.AddRange(patch.Where(x => x.Status == ChangeKind.Modified || x.Status == ChangeKind.Added).Select(x => Path.GetFileName(x.Path)));
            }
        }

        private void InitRepo(string repoPath)
        {
            if (Repository.IsValid(repoPath))
            {
                _repo = new Repository(repoPath);
            }
        }

        private (string projectsPath, string branchToSearch, string dateFrom, string dateTo, string authorEmail, string destinationOutputPath) GetConfiguration ()
        {
            var section = _configuration.GetSection("SearcherInfo");

            if (section == null) throw new InvalidOperationException("section SearcherInfo dosen't exists");

            var projectsPath = section.GetSection("ProjectsPath").Value;
            var searchedBranch = section.GetSection("SearchedBranch").Value;
            var dateFrom = section.GetSection("dateFrom").Value;
            var dateTo = section.GetSection("dateTo").Value;
            var authorEmail = section.GetSection("AuthorEmail").Value;
            var destinationOutputPath = section.GetSection("DestinationOutputPath").Value;

            return (projectsPath, searchedBranch, dateFrom, dateTo, authorEmail, destinationOutputPath);
        }


    }
}