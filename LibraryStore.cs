using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ClipboardTrail
{
    public sealed class LibraryStore
    {
        private readonly object gate = new object();
        private readonly List<LibraryNode> nodes = new List<LibraryNode>();

        public LibraryStore()
        {
            Load();
        }

        private void Load()
        {
            Directory.CreateDirectory(AppData.DirectoryPath);
            lock (gate) nodes.Clear();
            try
            {
                if (!File.Exists(AppData.LibraryPath)) return;
                List<LibraryNode> loaded = AppData.CreateSerializer().Deserialize<List<LibraryNode>>(File.ReadAllText(AppData.LibraryPath, Encoding.UTF8));
                if (loaded != null) nodes.AddRange(loaded.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id)));
            }
            catch { }
        }

        public LibraryNode Add(string name, string type, string parentId)
        {
            LibraryNode node = new LibraryNode
            {
                Id = Guid.NewGuid().ToString("N"),
                ParentId = parentId,
                Name = string.IsNullOrWhiteSpace(name) ? (type == "folder" ? "新文件夹" : type == "book" ? "新书籍" : "新文档") : name.Trim(),
                NodeType = type == "folder" ? "folder" : type == "book" ? "book" : "document",
                CreatedAt = DateTime.Now
            };
            lock (gate) { nodes.Add(node); Save(); }
            return node.Clone();
        }

        public LibraryNode ImportFile(string sourcePath, string parentId)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) throw new FileNotFoundException("找不到要导入的文件。", sourcePath);
            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            string[] allowed = new string[] { ".pdf", ".docx", ".doc", ".md", ".markdown", ".txt", ".epub", ".mobi", ".azw3", ".aw3" };
            if (!allowed.Contains(extension)) throw new InvalidOperationException("暂不支持该格式：" + extension);
            string id = Guid.NewGuid().ToString("N");
            string nodeDirectory = Path.Combine(AppData.LibraryFilesPath, id);
            Directory.CreateDirectory(nodeDirectory);
            string safeName = Path.GetFileName(sourcePath);
            string archivedPath = Path.Combine(nodeDirectory, safeName);
            File.Copy(sourcePath, archivedPath, true);
            bool ebook = extension == ".epub" || extension == ".mobi" || extension == ".azw3" || extension == ".aw3";
            LibraryNode node = new LibraryNode
            {
                Id = id,
                ParentId = parentId,
                Name = Path.GetFileNameWithoutExtension(sourcePath),
                NodeType = ebook ? "book" : "document",
                CreatedAt = DateTime.Now,
                FilePath = archivedPath,
                FileFormat = extension.TrimStart('.').ToUpperInvariant(),
                OriginalFileName = safeName
            };
            lock (gate) { nodes.Add(node); Save(); }
            return node.Clone();
        }

        public LibraryNode CreateLiveFolder(string sourcePath, string parentId)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath)) throw new DirectoryNotFoundException("找不到要同步的文件夹：" + sourcePath);
            string fullPath = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            lock (gate)
            {
                LibraryNode existing = nodes.FirstOrDefault(x => x.IsLiveFolder && string.Equals(NormalizePath(x.SyncSourcePath), NormalizePath(fullPath), StringComparison.OrdinalIgnoreCase));
                if (existing != null) return existing.Clone();
                string name = new DirectoryInfo(fullPath).Name;
                if (string.IsNullOrWhiteSpace(name)) name = fullPath;
                string id = Guid.NewGuid().ToString("N");
                LibraryNode node = new LibraryNode
                {
                    Id = id,
                    ParentId = parentId,
                    Name = name,
                    NodeType = "folder",
                    CreatedAt = DateTime.Now,
                    SyncMode = "live",
                    SyncSourcePath = fullPath,
                    SyncRootId = id,
                    SyncRelativePath = ""
                };
                nodes.Add(node);
                Save();
                return node.Clone();
            }
        }

        public LibraryNode GetLiveRootForNode(string nodeId)
        {
            lock (gate)
            {
                LibraryNode node = nodes.FirstOrDefault(x => x.Id == nodeId);
                if (node == null) return null;
                string rootId = string.IsNullOrWhiteSpace(node.SyncRootId) ? node.Id : node.SyncRootId;
                LibraryNode root = nodes.FirstOrDefault(x => x.Id == rootId && x.IsLiveFolder);
                return root == null ? null : root.Clone();
            }
        }

        public void DisableLiveFolder(string rootId)
        {
            lock (gate)
            {
                LibraryNode root = nodes.FirstOrDefault(x => x.Id == rootId && x.IsLiveFolder);
                if (root == null) return;
                foreach (LibraryNode node in nodes.Where(x => x.SyncRootId == rootId))
                {
                    node.SyncMode = null;
                    node.SyncSourcePath = null;
                    node.SyncRootId = null;
                    node.SyncRelativePath = null;
                    node.SyncLength = 0;
                    node.SyncLastWriteUtc = null;
                }
                Save();
            }
        }

        internal LiveFolderSyncResult SyncLiveFolder(string rootId)
        {
            LiveFolderSyncResult result = new LiveFolderSyncResult { RootId = rootId };
            LibraryNode root;
            lock (gate) root = nodes.FirstOrDefault(x => x.Id == rootId && x.IsLiveFolder) == null ? null : nodes.First(x => x.Id == rootId && x.IsLiveFolder).Clone();
            if (root == null) { result.Errors.Add("找不到活动文件夹。"); return result; }
            string sourceRoot = NormalizePath(root.SyncSourcePath);
            if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
            {
                result.SourceUnavailable = true;
                result.Errors.Add("源文件夹当前不可用，已保留资料库中的现有文件：" + (root.SyncSourcePath ?? ""));
                return result;
            }

            List<string> directories = new List<string>();
            List<string> files = new List<string>();
            ScanLiveFolder(sourceRoot, "", directories, files, result.Errors);
            bool completeScan = result.Errors.Count == 0;
            directories.Sort(StringComparer.OrdinalIgnoreCase);
            files.Sort(StringComparer.OrdinalIgnoreCase);

            lock (gate)
            {
                LibraryNode currentRoot = nodes.FirstOrDefault(x => x.Id == rootId && x.IsLiveFolder);
                if (currentRoot == null) { result.Errors.Add("同步过程中活动文件夹已被移除。"); return result; }
                Dictionary<string, LibraryNode> existingByRelative = nodes.Where(x => x.SyncRootId == rootId)
                    .GroupBy(x => NormalizeRelativePath(x.SyncRelativePath), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
                Dictionary<string, string> folderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "", rootId } };
                HashSet<string> seenIds = new HashSet<string> { rootId };

                foreach (string relative in directories.OrderBy(x => RelativeDepth(x)).ThenBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    LibraryNode folder;
                    if (!existingByRelative.TryGetValue(relative, out folder) || !folder.IsFolder)
                    {
                        folder = new LibraryNode
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            ParentId = folderIds[ParentRelativePath(relative)],
                            Name = RelativeName(relative),
                            NodeType = "folder",
                            CreatedAt = DateTime.Now,
                            SyncRootId = rootId,
                            SyncRelativePath = relative
                        };
                        nodes.Add(folder);
                        result.AddedFolders++;
                    }
                    else
                    {
                        folder.ParentId = folderIds[ParentRelativePath(relative)];
                        folder.Name = RelativeName(relative);
                    }
                    folderIds[relative] = folder.Id;
                    seenIds.Add(folder.Id);
                }

                foreach (string relative in files)
                {
                    string fullSource = Path.Combine(sourceRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                    FileInfo info;
                    try { info = new FileInfo(fullSource); }
                    catch (Exception ex) { result.Errors.Add(fullSource + "：" + ex.Message); continue; }
                    LibraryNode fileNode;
                    if (!existingByRelative.TryGetValue(relative, out fileNode) || !fileNode.IsDocument)
                    {
                        try
                        {
                            fileNode = CreateSyncedFileNode(fullSource, folderIds[ParentRelativePath(relative)], rootId, relative, info);
                            nodes.Add(fileNode);
                            result.AddedFiles++;
                        }
                        catch (Exception ex) { result.Errors.Add(fullSource + "：" + ex.Message); continue; }
                    }
                    else
                    {
                        seenIds.Add(fileNode.Id);
                        bool changed = fileNode.SyncLength != info.Length || !fileNode.SyncLastWriteUtc.HasValue || fileNode.SyncLastWriteUtc.Value != info.LastWriteTimeUtc;
                        if (changed)
                        {
                            try
                            {
                                CopySyncedFile(fullSource, fileNode);
                                fileNode.SyncLength = info.Length;
                                fileNode.SyncLastWriteUtc = info.LastWriteTimeUtc;
                                fileNode.Name = Path.GetFileNameWithoutExtension(fullSource);
                                fileNode.OriginalFileName = Path.GetFileName(fullSource);
                                fileNode.FileFormat = Path.GetExtension(fullSource).TrimStart('.').ToUpperInvariant();
                                InvalidateReaderCache(Path.GetDirectoryName(fileNode.FilePath));
                                result.UpdatedFiles++;
                            }
                            catch (Exception ex) { result.Errors.Add(fullSource + "：" + ex.Message); continue; }
                        }
                        fileNode.ParentId = folderIds[ParentRelativePath(relative)];
                    }
                    fileNode.SyncRootId = rootId;
                    fileNode.SyncRelativePath = relative;
                    seenIds.Add(fileNode.Id);
                }

                if (completeScan && result.Errors.Count == 0)
                {
                    List<LibraryNode> missing = nodes.Where(x => x.SyncRootId == rootId && x.Id != rootId && !seenIds.Contains(x.Id))
                        .OrderByDescending(x => RelativeDepth(x.SyncRelativePath)).ToList();
                    foreach (LibraryNode missingNode in missing)
                    {
                        if (missingNode.IsDocument) { result.RemovedFiles++; TryDeleteArchivedNode(missingNode); }
                        else result.RemovedFolders++;
                        result.RemovedNodeIds.Add(missingNode.Id);
                        nodes.Remove(missingNode);
                    }
                }
                Save();
            }
            return result;
        }

        private static LibraryNode CreateSyncedFileNode(string sourcePath, string parentId, string rootId, string relative, FileInfo info)
        {
            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            string id = Guid.NewGuid().ToString("N");
            string nodeDirectory = Path.Combine(AppData.LibraryFilesPath, id);
            Directory.CreateDirectory(nodeDirectory);
            string archivedPath = Path.Combine(nodeDirectory, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, archivedPath, true);
            bool ebook = extension == ".epub" || extension == ".mobi" || extension == ".azw3" || extension == ".aw3";
            return new LibraryNode
            {
                Id = id,
                ParentId = parentId,
                Name = Path.GetFileNameWithoutExtension(sourcePath),
                NodeType = ebook ? "book" : "document",
                CreatedAt = DateTime.Now,
                FilePath = archivedPath,
                FileFormat = extension.TrimStart('.').ToUpperInvariant(),
                OriginalFileName = Path.GetFileName(sourcePath),
                SyncRootId = rootId,
                SyncRelativePath = relative,
                SyncLength = info.Length,
                SyncLastWriteUtc = info.LastWriteTimeUtc
            };
        }

        private static void CopySyncedFile(string sourcePath, LibraryNode node)
        {
            string destination = node.FilePath;
            if (string.IsNullOrWhiteSpace(destination))
            {
                string nodeDirectory = Path.Combine(AppData.LibraryFilesPath, node.Id);
                Directory.CreateDirectory(nodeDirectory);
                destination = Path.Combine(nodeDirectory, Path.GetFileName(sourcePath));
                node.FilePath = destination;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temp = destination + ".syncing";
            File.Copy(sourcePath, temp, true);
            if (File.Exists(destination)) File.Delete(destination);
            File.Move(temp, destination);
        }

        private static void ScanLiveFolder(string sourceRoot, string relativeFolder, List<string> directories, List<string> files, List<string> errors)
        {
            string fullFolder = relativeFolder.Length == 0 ? sourceRoot : Path.Combine(sourceRoot, relativeFolder.Replace('/', Path.DirectorySeparatorChar));
            string[] childDirectories;
            string[] childFiles;
            try
            {
                childDirectories = Directory.GetDirectories(fullFolder);
                childFiles = Directory.GetFiles(fullFolder);
            }
            catch (Exception ex) { errors.Add(fullFolder + "：" + ex.Message); return; }
            foreach (string child in childDirectories)
            {
                try { if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue; }
                catch (Exception ex) { errors.Add(child + "：" + ex.Message); continue; }
                string relative = CombineRelative(relativeFolder, Path.GetFileName(child));
                directories.Add(relative);
                ScanLiveFolder(sourceRoot, relative, directories, files, errors);
            }
            foreach (string child in childFiles) if (IsSupportedFile(child)) files.Add(CombineRelative(relativeFolder, Path.GetFileName(child)));
        }

        private static bool IsSupportedFile(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".pdf" || extension == ".docx" || extension == ".doc" || extension == ".md" || extension == ".markdown" || extension == ".txt" || extension == ".epub" || extension == ".mobi" || extension == ".azw3" || extension == ".aw3";
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return path.Trim(); }
        }

        private static string NormalizeRelativePath(string path) { return (path ?? "").Replace('\\', '/').Trim('/'); }
        private static string CombineRelative(string parent, string name) { return NormalizeRelativePath(string.IsNullOrEmpty(parent) ? name : parent + "/" + name); }
        private static int RelativeDepth(string path) { return string.IsNullOrEmpty(path) ? 0 : NormalizeRelativePath(path).Count(x => x == '/') + 1; }
        private static string ParentRelativePath(string path)
        {
            string value = NormalizeRelativePath(path);
            int slash = value.LastIndexOf('/');
            return slash < 0 ? "" : value.Substring(0, slash);
        }
        private static string RelativeName(string path)
        {
            string value = NormalizeRelativePath(path);
            int slash = value.LastIndexOf('/');
            return slash < 0 ? value : value.Substring(slash + 1);
        }

        private static void TryDeleteArchivedNode(LibraryNode node)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(node.FilePath)) return;
                string directory = Path.GetDirectoryName(node.FilePath);
                string allowedRoot = Path.GetFullPath(AppData.LibraryFilesPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (fullDirectory.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch { }
        }

        public LibraryNode FindDuplicateFile(string sourcePath, string parentId)
        {
            string name = Path.GetFileName(sourcePath ?? "");
            if (name.Length == 0) return null;
            lock (gate)
            {
                LibraryNode found = nodes.FirstOrDefault(x => x.IsDocument && string.Equals(x.ParentId ?? "", parentId ?? "", StringComparison.Ordinal) && string.Equals(x.OriginalFileName ?? Path.GetFileName(x.FilePath), name, StringComparison.CurrentCultureIgnoreCase));
                return found == null ? null : found.Clone();
            }
        }

        public LibraryNode FindFolder(string name, string parentId)
        {
            lock (gate)
            {
                LibraryNode found = nodes.FirstOrDefault(x => x.IsFolder && string.Equals(x.ParentId ?? "", parentId ?? "", StringComparison.Ordinal) && string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase));
                return found == null ? null : found.Clone();
            }
        }

        public LibraryNode ReplaceFile(string id, string sourcePath, bool preservePreviousVersion)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) throw new FileNotFoundException("找不到要导入的文件。", sourcePath);
            lock (gate)
            {
                LibraryNode node = nodes.FirstOrDefault(x => x.Id == id && x.IsDocument);
                if (node == null || string.IsNullOrWhiteSpace(node.FilePath)) throw new InvalidOperationException("找不到要更新的资料。");
                string nodeDirectory = Path.GetDirectoryName(node.FilePath);
                Directory.CreateDirectory(nodeDirectory);
                if (preservePreviousVersion && File.Exists(node.FilePath) && !FilesEqual(node.FilePath, sourcePath))
                {
                    string versionDirectory = Path.Combine(nodeDirectory, "versions");
                    Directory.CreateDirectory(versionDirectory);
                    string versionName = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Path.GetFileName(node.FilePath);
                    File.Copy(node.FilePath, Path.Combine(versionDirectory, versionName), false);
                }
                if (!string.Equals(Path.GetFullPath(node.FilePath), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase)) File.Copy(sourcePath, node.FilePath, true);
                node.Name = Path.GetFileNameWithoutExtension(sourcePath);
                node.OriginalFileName = Path.GetFileName(sourcePath);
                node.FileFormat = Path.GetExtension(sourcePath).TrimStart('.').ToUpperInvariant();
                InvalidateReaderCache(nodeDirectory);
                Save();
                return node.Clone();
            }
        }

        private static bool FilesEqual(string first, string second)
        {
            FileInfo a = new FileInfo(first), b = new FileInfo(second);
            if (a.Length != b.Length) return false;
            using (SHA256 sha = SHA256.Create())
            using (FileStream left = File.OpenRead(first))
            using (FileStream right = File.OpenRead(second)) return sha.ComputeHash(left).SequenceEqual(sha.ComputeHash(right));
        }

        private static void InvalidateReaderCache(string nodeDirectory)
        {
            foreach (string path in Directory.GetFiles(nodeDirectory, "reader-v*.html", SearchOption.TopDirectoryOnly)) try { File.Delete(path); } catch { }
            string epub = Path.Combine(nodeDirectory, "epub-content");
            if (Directory.Exists(epub)) try { Directory.Delete(epub, true); } catch { }
            string converted = Path.Combine(nodeDirectory, "calibre-converted.epub");
            if (File.Exists(converted)) try { File.Delete(converted); } catch { }
        }

        public void Rename(string id, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            lock (gate)
            {
                LibraryNode node = nodes.FirstOrDefault(x => x.Id == id);
                if (node == null) return;
                node.Name = name.Trim();
                Save();
            }
        }

        public bool Move(string id, string newParentId)
        {
            lock (gate)
            {
                LibraryNode node = nodes.FirstOrDefault(x => x.Id == id);
                if (node == null || id == newParentId) return false;
                LibraryNode parent = string.IsNullOrEmpty(newParentId) ? null : nodes.FirstOrDefault(x => x.Id == newParentId);
                if (parent != null && !parent.IsFolder) return false;
                string cursor = newParentId;
                while (!string.IsNullOrEmpty(cursor))
                {
                    if (cursor == id) return false;
                    LibraryNode current = nodes.FirstOrDefault(x => x.Id == cursor);
                    cursor = current == null ? null : current.ParentId;
                }
                node.ParentId = newParentId;
                Save();
                return true;
            }
        }

        public List<string> DeleteBranch(string id)
        {
            lock (gate)
            {
                HashSet<string> ids = new HashSet<string>();
                CollectBranch(id, ids);
                nodes.RemoveAll(x => ids.Contains(x.Id));
                Save();
                return ids.ToList();
            }
        }

        private void CollectBranch(string id, HashSet<string> ids)
        {
            if (!ids.Add(id)) return;
            foreach (LibraryNode child in nodes.Where(x => x.ParentId == id).ToList()) CollectBranch(child.Id, ids);
        }

        public List<LibraryNode> Snapshot()
        {
            lock (gate) return nodes.Select(x => x.Clone()).ToList();
        }

        public void Reload() { Load(); }

        internal void ReplaceAll(IEnumerable<LibraryNode> replacement)
        {
            lock (gate)
            {
                nodes.Clear();
                if (replacement != null) nodes.AddRange(replacement.Where(x => x != null).Select(x => x.Clone()));
                Save();
            }
        }

        public List<string> GetDocumentIdsInBranch(string id)
        {
            List<LibraryNode> copy = Snapshot();
            HashSet<string> branch = new HashSet<string>(GetNodeIdsInBranch(id));
            return copy.Where(x => x.IsDocument && branch.Contains(x.Id)).Select(x => x.Id).ToList();
        }

        public List<string> GetNodeIdsInBranch(string id)
        {
            List<LibraryNode> copy = Snapshot();
            HashSet<string> branch = new HashSet<string>();
            Action<string> collect = null;
            collect = delegate(string parent)
            {
                if (!branch.Add(parent)) return;
                foreach (LibraryNode child in copy.Where(x => x.ParentId == parent)) collect(child.Id);
            };
            collect(id);
            return branch.ToList();
        }

        internal List<DocumentChoice> GetDocumentChoices()
        {
            List<LibraryNode> copy = Snapshot();
            return copy.Where(x => x.IsDocument).Select(x => new DocumentChoice { Id = x.Id, Path = BuildPath(x, copy) }).OrderBy(x => x.Path).ToList();
        }

        public string GetPath(string id)
        {
            List<LibraryNode> copy = Snapshot();
            LibraryNode node = copy.FirstOrDefault(x => x.Id == id);
            return node == null ? "未归档" : BuildPath(node, copy);
        }

        private static string BuildPath(LibraryNode node, List<LibraryNode> all)
        {
            List<string> parts = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            LibraryNode current = node;
            while (current != null && seen.Add(current.Id))
            {
                parts.Insert(0, current.Name);
                current = string.IsNullOrEmpty(current.ParentId) ? null : all.FirstOrDefault(x => x.Id == current.ParentId);
            }
            return string.Join(" / ", parts.ToArray());
        }

        private void Save()
        {
            Directory.CreateDirectory(AppData.DirectoryPath);
            string json = AppData.CreateSerializer().Serialize(nodes);
            string temp = AppData.LibraryPath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(AppData.LibraryPath)) File.Delete(AppData.LibraryPath);
            File.Move(temp, AppData.LibraryPath);
        }
    }
}
