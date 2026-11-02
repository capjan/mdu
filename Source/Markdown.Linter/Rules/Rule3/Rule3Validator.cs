using System.Text.RegularExpressions;
using FluentValidation;
using Markdown.Linter;
using Markdown.Linter.Paths;

namespace Markdown.Linter.Rules.Rule3;

/// <summary>
/// Validator that checks if the casing of internal links are correct and match the casing on the current machine.
/// </summary>
public class Rule3Validator : AbstractValidator<Rule3Input>
{
    public Rule3Validator()
    {
        RuleFor(x => x.LinkDestination).Must(BeCasedCorrectly)
            .WithMessage(item => $"{RuleCodes.LinkCasing}: Link target '{item.LinkDestination}' does not match the file-system casing.");
    }

    private bool BeCasedCorrectly(Rule3Input context, string linkDestination)
    {
        if (Regex.IsMatch(linkDestination, "^https?://", RegexOptions.IgnoreCase))
        {
            // we do not check the casing of external links, so we return true to avoid a violation of the rule.
            return true;
        }
        var fileInfo = new FileInfo(context.DocumentFilePath);

        // if the file does not exists the casing can't be checked. So we're done.
        if (!fileInfo.Exists) return true;
        
        
        var suffixStart = linkDestination.IndexOfAny(['?', '#']);
        var relativePath = suffixStart < 0 ? linkDestination : linkDestination[..suffixStart];
        if (string.IsNullOrEmpty(relativePath)) return true;

        relativePath = Uri.UnescapeDataString(relativePath);

        var markdownPath = SuperiorPath.Create(context.DocumentFilePath);
        var linkPath = SuperiorPath.Create(relativePath);
        if (linkPath.IsRooted) return true;
        
        // Loop through the Elements and check if they are cased correctly
        var directoryInfo = new DirectoryInfo(markdownPath.Directory.FullPath);
        foreach (var pathElement in linkPath)
        {
            if (pathElement.ElementType == PathElementType.Parent)
            {
                var parent = directoryInfo.Parent;
                if (parent == null)
                {
                    // When the directory does not exists we can not check it's casing, but it does not violate the
                    // check for the casing, so we return true
                    return true;
                }
                directoryInfo = parent;
            }
            else if (pathElement.ElementType == PathElementType.Current)
            {
                // nothing to do
            }
            else if (pathElement.ElementType == PathElementType.Folder)
            {
                var matchingDirectory = directoryInfo.GetDirectories().FirstOrDefault(di => di.Name.Equals(pathElement.Name, StringComparison.OrdinalIgnoreCase));
                if (matchingDirectory == null)
                {
                    // if we do not find a directory we are done. We do not find the directory, but it does not violate
                    // the casing rule, so we return true
                    return true;
                }

                if (!matchingDirectory.Name.Equals(pathElement.Name, StringComparison.Ordinal)) return false;
                
                directoryInfo = matchingDirectory;
            }
            else if (pathElement.ElementType == PathElementType.File)
            {
                var matchingFile = directoryInfo.GetFiles().FirstOrDefault(fi =>
                    fi.Name.Equals(pathElement.Name, StringComparison.OrdinalIgnoreCase));
                if (matchingFile == null)
                {
                    // Not finding the file is bad, but does not violate this rule.
                    // Nevertheless, we are finished here, as we can't continue.
                    return true;
                }
                if (!matchingFile.Name.Equals(pathElement.Name, StringComparison.Ordinal)) return false;
                return true;
            }
            else if (pathElement.ElementType == PathElementType.Unknown)
            {
                var matchingDirectory = directoryInfo.GetDirectories().FirstOrDefault(di => di.Name.Equals(pathElement.Name, StringComparison.OrdinalIgnoreCase));
                var matchingFile = directoryInfo.GetFiles().FirstOrDefault(fi =>
                    fi.Name.Equals(pathElement.Name, StringComparison.OrdinalIgnoreCase));
                if (matchingFile == null && matchingDirectory == null)
                {
                    // Not finding the file is bad, but does not violate this rule.
                    // Nevertheless, we are finished here, as we can't continue.
                    return true;
                }
                if (matchingFile != null && !matchingFile.Name.Equals(pathElement.Name, StringComparison.Ordinal)) return false;
                if (matchingDirectory != null)
                {
                    if (!matchingDirectory.Name.Equals(pathElement.Name, StringComparison.Ordinal)) return false;
                    directoryInfo = matchingDirectory;
                    continue;
                }
                return true;
            }
            
        }
        
        return true;
    }
    
    
}
