using System.Collections;
using System.Text.RegularExpressions;

namespace Markdown.Linter.Paths;

public class SuperiorPath : IEnumerable<PathElement>
{
    public readonly bool IsRooted;
    public readonly bool IsFolder;
    public readonly bool IsFile;
    private readonly IList<PathElement> _elements;
    public readonly bool IsWindowsPath;
    public readonly string WindowsDriveLetter;

    private SuperiorPath(bool isRooted, IEnumerable<PathElement> elements)
    {
        IsWindowsPath = false;
        WindowsDriveLetter = "";
        IsRooted = isRooted;
        _elements = elements as IList<PathElement> ?? new List<PathElement>(elements);
        var lastElement = _elements.LastOrDefault();
        IsFolder = lastElement.ElementType == PathElementType.Folder;
        IsFile = lastElement.ElementType == PathElementType.File;
    }
    
    private SuperiorPath(string windowsDriveLetter, IEnumerable<PathElement> elements)
    {
        IsWindowsPath = true;
        WindowsDriveLetter = windowsDriveLetter;
        IsRooted = true;
        _elements = elements as IList<PathElement> ?? new List<PathElement>(elements);
        var lastElement = _elements.LastOrDefault();
        IsFolder = lastElement.ElementType == PathElementType.Folder;
        IsFile = lastElement.ElementType == PathElementType.File;
    }
    

    // New method to concatenate a string to SuperiorPath
    public SuperiorPath Concatenate(string path)
    {
        var elementsToAdd = path.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var isPathEndingWithSlash = path.EndsWith("/");
        var lastIndex = elementsToAdd.Length - 1;
        var lastElementIsFolder = isPathEndingWithSlash;
        var pathElementsToAdd = elementsToAdd.Select((item, index) => new PathElement(item, index == lastIndex && !lastElementIsFolder ? PathElementType.File : PathElementType.Folder));
        var newElements = _elements.Concat(pathElementsToAdd).ToArray();
        return new SuperiorPath(IsRooted, newElements);
    }
    
    // Overloading the division operator (/)
    public static SuperiorPath operator /(SuperiorPath superiorPath, string path)
    {
        return superiorPath.Concatenate(path);
    }
    
    public IEnumerator<PathElement> GetEnumerator() => _elements.GetEnumerator();

    public string FullPath
    {
        get
        {
            if (IsWindowsPath)
            {
                var winPrefix = IsRooted ? WindowsDriveLetter + ":\\" : "";
                var winPostfix = IsFolder ? "\\" : ""; 
                return winPrefix + string.Join("\\", _elements.Select(i => i.Name)) + winPostfix;
                
            }
            
            var prefix = IsRooted ? "/" : "";
            var postfix = IsFolder ? "/" : ""; 
            return prefix + string.Join("/", _elements.Select(i => i.Name)) + postfix;
        }
    }

    public string FileName
    {
        get
        {
            if (_elements.Last() is { ElementType: PathElementType.File } lastElement)
            {
                return lastElement.Name;
            }
            return "";
        }
    }

    public SuperiorPath Directory
    {
        get
        {
            if (IsFolder) return this;
            var elementsExcludingLast = _elements.Take(_elements.Count - 1).ToList();
            return new SuperiorPath(IsRooted, elementsExcludingLast);
        }
    }

    public override string ToString()
    {
        return FullPath;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public static SuperiorPath Root = Create("/");
    
    public static SuperiorPath Create(string filePath)
    {
        var m = Regex.Match(filePath, @"^(?<drive>[A-Z]):\\");
        if (m.Success)
        {
            var windowsDriveLetter = m.Groups["drive"].Value;
            filePath = filePath[2..];
            var elements = filePath.Split('\\', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var lastIndex = elements.Length - 1;
            var pathElements = elements.Select((itm, index) =>
            {
                switch (itm)
                {
                    case "..":
                        return new PathElement(itm, PathElementType.Parent);
                    case ".":
                        return new PathElement(".", PathElementType.Current);
                }

                if (index < lastIndex)
                {
                    return new PathElement(itm, PathElementType.Folder);
                }

                if (index == lastIndex && !filePath.EndsWith("\\"))
                {
                    return new PathElement(itm, PathElementType.File);
                }
                return new PathElement(itm, PathElementType.Unknown);
            }).ToArray();
    
            return new SuperiorPath(windowsDriveLetter: windowsDriveLetter, pathElements);
        }
        else
        {
            return CreateMacOSPath(filePath);
        }
    }

    private static SuperiorPath CreateMacOSPath(string filePath)
    {
        var elements = filePath.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var isRooted = filePath.StartsWith("/");
        var lastIndex = elements.Length - 1;
        var pathElements = elements.Select((itm, index) =>
        {
            switch (itm)
            {
                case "..":
                    return new PathElement(itm, PathElementType.Parent);
                case ".":
                    return new PathElement(".", PathElementType.Current);
            }

            if (index < lastIndex)
            {
                return new PathElement(itm, PathElementType.Folder);
            }

            if (index == lastIndex && !filePath.EndsWith("/"))
            {
                return new PathElement(itm, PathElementType.File);
            }
            return new PathElement(itm, PathElementType.Unknown);
        }).ToArray();
    
        return new SuperiorPath(isRooted, pathElements);
    }
}

