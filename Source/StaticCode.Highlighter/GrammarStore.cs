namespace StaticCode.Highlighter;

public static class GrammarStore
{
    private static readonly string[] JavaScriptTypeScriptOperators =
    {
        "===", "!==", ">>>", "**=", "...", "=>", "==", "!=", "<=", ">=", "++", "--", "&&", "||",
        "??", "?.", "**", "<<", ">>", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "=", "+", "-",
        "*", "/", "%", "<", ">", "!", "~", "&", "|", "^", "?", ":", ";", ",", ".", "(", ")", "[",
        "]", "{", "}"
    };
    private static readonly LanguageString[] JavaScriptTypeScriptStrings =
    {
        new LanguageString('"', "\\\"", "\\\\"),
        new LanguageString('\'', "\\\'", "\\\\"),
        new LanguageString('`', "\\`", "\\\\")
    };

    /// <summary>
    /// Returns the Language Settings for the given language name followed in markdown after the tree backticks
    /// </summary>
    /// <param name="name">name of the language <see href="https://rdmd.readme.io/docs/code-blocks"/></param>
    /// <returns>The resolved language setting or plain if not found</returns>
    public static Grammar GetByName(string name) => GetFactory(name)();

    // The finite grammar factories also identify lexer cache entries: aliases share one entry, and
    // arbitrary unknown code-fence names all use Plain without retaining their names.
    internal static Func<Grammar> GetFactory(string name) => name switch
    {
        "cs" or "csharp" => CSharp,
        "swift" => Swift,
        "js" or "javascript" or "jsx" => JavaScript,
        "ts" or "typescript" => TypeScript,
        "py" or "python" => Python,
        "kt" or "kotlin" => Kotlin,
        "json" => Json,
        "sql" => Sql,
        "html" or "htm" => Html,
        "css" => Css,
        "java" => Java,
        "rs" or "rust" => Rust,
        "go" or "golang" => Go,
        "yaml" or "yml" => Yaml,
        "bash" or "zsh" or "sh" or "shell" => Shell,
        "md" or "markdown" => Markdown,
        _ => Plain
    };

    private static Grammar Markdown()
    {
        return new Grammar(
            keywords: Array.Empty<string>(),
            operators: Array.Empty<string>(),
            strings: Array.Empty<LanguageString>(),
            lineCommentStart: string.Empty,
            highlightMarkdown: true);
    }
    
    private static Grammar CSharp()
    {
        return new Grammar(
            keywords: new[]
            {
                "if", "var", "int", "string", "return"
            },
            operators: new[]
            {
                "-", "+", "/", "*", "==", "=", ";"
            },
            strings: new []{
                new LanguageString(
                    startEnd: '"',
                    escapeSequences: "\\\"")
            },
            lineCommentStart: "//",
            blockCommentStart: "/*",
            blockCommentEnd: "*/");
    }
    
    private static Grammar Swift()
    {
        return new Grammar(
            keywords: new[]
            {
                "if", "let", "var", "do", "try", "catch", "Int", "String", "return"
            },
            operators: new[]
            {
                "-", "+", "/", "*", "==", "=", ";"
            },
            strings: new []{
                new LanguageString(
                    startEnd: '"',
                    escapeSequences: "\\\"")
            },
            lineCommentStart: "//",
            blockCommentStart: "/*",
            blockCommentEnd: "*/");
    }

    private static Grammar TypeScript()
    {
        return new Grammar(
            keywords: new[]
            {
                "abstract", "any", "as", "async", "await", "boolean", "break", "case", "catch", "class",
                "const", "constructor", "continue", "debugger", "declare", "default", "delete", "do", "else",
                "enum", "export", "extends", "false", "finally", "for", "from", "function", "get", "if",
                "implements", "import", "in", "instanceof", "interface", "keyof", "let", "module", "namespace",
                "never", "new", "null", "number", "object", "private", "protected", "public", "readonly",
                "return", "set", "static", "string", "super", "switch", "symbol", "this", "throw", "true",
                "try", "type", "typeof", "undefined", "unique", "unknown", "var", "void", "while", "with",
                "yield"
            },
            operators: JavaScriptTypeScriptOperators,
            strings: JavaScriptTypeScriptStrings,
            lineCommentStart: "//",
            blockCommentStart: "/*",
            blockCommentEnd: "*/");
    }

    private static Grammar Python()
    {
        return new Grammar(
            keywords: new[]
            {
                "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue",
                "def", "del", "elif", "else", "except", "finally", "for", "from", "global", "if", "import",
                "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try", "while",
                "with", "yield"
            },
            operators: new[]
            {
                "**=", "//=", ">>=", "<<=", ":=", "==", "!=", "<=", ">=", "+=", "-=", "*=", "/=", "%=",
                "&=", "|=", "^=", "**", "//", "<<", ">>", "=", "+", "-", "*", "/", "%", "<", ">", "&",
                "|", "^", "~", ":", ";", ",", ".", "(", ")", "[", "]", "{", "}"
            },
            strings: new[]
            {
                new LanguageString('"', "\\\"", "\\\\"),
                new LanguageString('\'', "\\\'", "\\\\")
            },
            lineCommentStart: "#");
    }

    private static Grammar Kotlin()
    {
        return new Grammar(
            keywords: new[]
            {
                "as", "break", "by", "catch", "class", "companion", "const", "constructor", "continue", "data",
                "delegate", "do", "else", "enum", "external", "false", "field", "final", "finally", "for",
                "fun", "get", "if", "import", "in", "infix", "init", "inline", "inner", "interface", "internal",
                "is", "lateinit", "noinline", "null", "object", "open", "operator", "out", "override", "package",
                "private", "property", "protected", "public", "receiver", "reified", "return", "sealed", "set",
                "suspend", "tailrec", "this", "throw", "true", "try", "typealias", "val", "var", "vararg",
                "when", "where", "while"
            },
            operators: new[]
            {
                "===", "!==", "!in", "!is", "..<", "..", "!!", "?.", "?:", "::", "->", "==", "!=", "<=", ">=",
                "+=", "-=", "*=", "/=", "%=", "++", "--", "&&", "||", "=", "+", "-", "*", "/", "%", "<", ">",
                "!", "?", ":", ";", ",", ".", "(", ")", "[", "]", "{", "}"
            },
            strings: new[]
            {
                new LanguageString('"', "\\\"", "\\\\"),
                new LanguageString('\'', true, "\\\'", "\\\\")
            },
            lineCommentStart: "//",
            blockCommentStart: "/*",
            blockCommentEnd: "*/");
    }

    private static Grammar JavaScript()
    {
        return new Grammar(
            keywords: new[]
            {
                "as", "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger",
                "default", "delete", "do", "else", "export", "extends", "false", "finally", "for", "from",
                "function", "if", "import", "in", "instanceof", "let", "new", "null", "of", "return", "static",
                "super", "switch", "this", "throw", "true", "try", "typeof", "undefined", "var", "void", "while",
                "with", "yield"
            },
            operators: JavaScriptTypeScriptOperators,
            strings: JavaScriptTypeScriptStrings,
            lineCommentStart: "//",
            blockCommentStart: "/*",
            blockCommentEnd: "*/");
    }

    private static Grammar Json()
    {
        return new Grammar(
            keywords: new[]
            {
                "false", "null", "true"
            },
            operators: new[]
            {
                ":", ",", "{", "}", "[", "]"
            },
            strings: new[]
            {
                new LanguageString('"', "\\\"", "\\\\", "\\/", "\\b", "\\f", "\\n", "\\r", "\\t")
            },
            lineCommentStart: string.Empty);
    }

    private static Grammar Sql()
    {
        return new Grammar(
            keywords: new[]
            {
                "and", "as", "asc", "between", "by", "case", "check", "commit", "constraint", "create",
                "delete", "desc", "distinct", "drop", "else", "end", "except", "exists", "false", "foreign",
                "from", "full", "group", "having", "in", "index", "inner", "insert", "intersect", "into",
                "is", "join", "key", "left", "like", "limit", "not", "null", "offset", "on", "or", "order",
                "outer", "primary", "recursive", "references", "rollback", "select", "set", "table", "then",
                "transaction", "true", "union", "unique", "update", "values", "view", "when", "where", "with"
            },
            operators: new[]
            {
                "<>", "!=", "<=", ">=", "||", "::", "=", "<", ">", "+", "-", "*", "/", "%", ".", ",", ";",
                "(", ")", "[", "]"
            },
            strings: new[]
            {
                new LanguageString('\'', "''"),
                new LanguageString('"', "\"\"")
            },
            lineCommentStart: "--",
            blockCommentStart: "/*",
            blockCommentEnd: "*/",
            ignoreKeywordCase: true);
    }

    private static Grammar Html()
    {
        return new Grammar(
            keywords: Array.Empty<string>(),
            operators: new[]
            {
                "</", "/>", "<", ">", "=", "/"
            },
            strings: Array.Empty<LanguageString>(),
            lineCommentStart: string.Empty,
            blockCommentStart: "<!--",
            blockCommentEnd: "-->",
            highlightHtml: true);
    }

    private static Grammar Css()
    {
        return new Grammar(
            keywords: Array.Empty<string>(),
            declarationKeywords: new[]
            {
                "align-items", "background-color", "background", "border-radius", "border", "color", "display",
                "flex", "font-family", "font-size", "font-weight", "gap", "grid", "height", "justify-content",
                "margin", "max-width", "padding", "position", "width"
            },
            atRules: new[]
            {
                "font-face", "import", "keyframes", "layer", "media", "supports"
            },
            operators: new[]
            {
                "::", ":", ";", ",", "{", "}", "(", ")", "[", "]", ">", "+", "~", "=", ".", "#", "*", "@"
            },
            strings: new[]
            {
                new LanguageString('"', "\\\"", "\\\\"),
                new LanguageString('\'', "\\\'", "\\\\")
            },
            lineCommentStart: string.Empty,
            ignoreKeywordCase: true,
            blockCommentStart: "/*",
            blockCommentEnd: "*/");
    }

    private static Grammar Java()
    {
        return new Grammar(
            keywords: new[]
            {
                "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class", "const",
                "continue", "default", "do", "double", "else", "enum", "extends", "final", "finally", "float",
                "for", "if", "implements", "import", "instanceof", "int", "interface", "long", "native", "new",
                "null", "package", "private", "protected", "public", "return", "short", "static", "strictfp",
                "super", "switch", "synchronized", "this", "throw", "throws", "transient", "try", "void", "volatile",
                "while", "true", "false", "var", "record", "yield", "sealed", "permits"
            },
            operators: new[]
            {
                ">>>=", "<<=", ">>=", "...", "->", "::", "==", "!=", "<=", ">=", "++", "--", "&&",
                "||", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", ">>>", "<<", ">>", "=", "+", "-", "*",
                "/", "%", "<", ">", "!", "~", "&", "|", "^", "?", ":", ";", ",", ".", "(", ")", "[", "]", "{", "}"
            },
            strings: new[]
            {
                new LanguageString("\"\"\"", "\\\"", "\\\\"),
                new LanguageString('"', "\\\"", "\\\\"),
                new LanguageString('\'', "\\\'", "\\\\")
            },
            lineCommentStart: "//",
            blockCommentStart: "/*",
            blockCommentEnd: "*/");
    }

    private static Grammar Rust()
    {
        return new Grammar(
            keywords: new[]
            {
                "Self", "as", "async", "await", "bool", "break", "const", "continue", "crate", "else", "enum",
                "extern", "false", "fn", "for", "if", "impl", "in", "let", "loop", "match", "mod", "move", "mut",
                "pub", "ref", "return", "self", "static", "struct", "super", "trait", "true", "type", "union",
                "unsafe", "use", "where", "while", "dyn"
            },
            operators: new[]
            {
                "..=", "...", "..", "::", "->", "=>", "==", "!=", "<=", ">=", "&&", "||", "<<=", ">>=", "+=", "-=",
                "*=", "/=", "%=", "&=", "|=", "^=", "<<", ">>", "=", "+", "-", "*", "/", "%", "<", ">", "!", "&",
                "|", "^", "~", "?", ":", ";", ",", ".", "(", ")", "[", "]", "{", "}"
            },
            strings: new[]
            {
                new LanguageString('"', "\\\"", "\\\\"),
                new LanguageString('\'', true, "\\\'", "\\\\")
            },
            lineCommentStart: "//",
            blockCommentStart: "/*",
            blockCommentEnd: "*/");
    }

    private static Grammar Go()
    {
        return new Grammar(
            keywords: new[]
            {
                "break", "case", "chan", "const", "continue", "default", "defer", "else", "fallthrough", "for",
                "func", "go", "goto", "if", "import", "interface", "map", "package", "range", "return", "select",
                "struct", "switch", "type", "var", "true", "false", "nil", "iota"
            },
            operators: new[]
            {
                "&^=", "<<=", ">>=", "...", "&^", ":=", "++", "--", "==", "!=", "<=", ">=", "&&", "||", "<-", "+=",
                "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<", ">>", "=", "+", "-", "*", "/", "%", "<", ">", "!",
                "&", "|", "^", "~", ":", ";", ",", ".", "(", ")", "[", "]", "{", "}"
            },
            strings: new[]
            {
                new LanguageString('"', "\\\"", "\\\\"),
                new LanguageString('\'', "\\\'", "\\\\"),
                new LanguageString('`')
            },
            lineCommentStart: "//",
            blockCommentStart: "/*",
            blockCommentEnd: "*/");
    }

    private static Grammar Yaml()
    {
        return new Grammar(
            keywords: new[]
            {
                "true", "false", "null"
            },
            operators: new[]
            {
                "-", "[", "]", "{", "}", ",", "?", "|", ">", "&", "*", "!"
            },
            strings: new[]
            {
                new LanguageString('"', "\\\"", "\\\\", "\\n", "\\t", "\\r"),
                new LanguageString('\'', "''")
            },
            lineCommentStart: "#",
            highlightKeys: true);
    }

    private static Grammar Shell()
    {
        return new Grammar(
            keywords: new[]
            {
                "if", "then", "else", "elif", "fi", "for", "while", "until", "do", "done",
                "case", "esac", "in", "function", "select", "time", "coproc"
            },
            operators: new[]
            {
                ";;&", ";;", ";&", "<<<", "&&", "||", ">>", "<<", "&>", ">&", "<&", ">|",
                "|", "&", ";", ">", "<", "(", ")", "{", "}", "!", "="
            },
            strings: new[]
            {
                new LanguageString('"', "\\\"", "\\\\"),
                new LanguageString('\'')
            },
            lineCommentStart: "#",
            highlightShellTokens: true);
    }

    private static Grammar Plain()
    {
        return new Grammar(
            keywords: Array.Empty<string>(),
            operators: Array.Empty<string>(),
            strings: Array.Empty<LanguageString>(),
            lineCommentStart: string.Empty
        );
    }


}
