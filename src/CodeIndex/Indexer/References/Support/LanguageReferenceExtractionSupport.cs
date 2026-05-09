using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class LanguageReferenceExtractionSupport
{
    private static readonly Regex CppIncludeRegex = new(
        @"^(?:\s*#\s*(?:include(?:_next)?|import)\s*(?:<(?<name>[^>\r\n]+)>|""(?<name>[^""\r\n]+)""|(?<name>[^\s]+))|\s*(?:export\s+)?import\s+(?:<(?<name>[^>\r\n]+)>|""(?<name>[^""\r\n]+)""|(?<name>:?[A-Za-z_]\w*(?:[.:][A-Za-z_]\w*)*))\s*;)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppBaseListRegex = new(
        @"^\s*(?:export\s+)?(?:(?:template|requires)\b[^{;]*\s+)*(?:class|struct)\s+[A-Za-z_]\w*(?:\s*final)?\s*:\s*(?<bases>[^{;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppNewTypeRegex = new(
        @"\bnew\s+(?<type>(?:[A-Za-z_]\w*\s*::\s*)*[A-Za-z_]\w*(?:\s*<[^;{}]+>)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppNamedCastTypeRegex = new(
        @"\b(?:static_cast|dynamic_cast|reinterpret_cast|const_cast)\s*<(?<type>[^;{}<>]+(?:<[^;{}<>]+>)?[^;{}<>]*)>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppCStyleCastTypeRegex = new(
        @"(?<![\w])\(\s*(?<type>(?:(?:const|volatile|typename|class|struct|enum)\s+)*(?:[A-Z_]\w*|[A-Za-z_]\w*\s*::\s*[A-Za-z_]\w*)(?:\s*<[^;{}()]+>)?(?:\s*[*&])*)\s*\)\s*(?:[A-Za-z_]\w*|\*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTypedefCastTypeRegex = new(
        @"(?<![\w])\(\s*(?<type>(?:(?:const|volatile|restrict|_Atomic)\s+)*[A-Za-z_]\w*_t(?:\s*\*)*)\s*\)\s*(?:[A-Za-z_]\w*|\*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTypedefSizeofTypeRegex = new(
        @"\bsizeof\s*\(\s*(?<type>(?:(?:const|volatile|restrict|_Atomic)\s+)*[A-Za-z_]\w*_t(?:\s*\*(?:\s*(?:const|volatile|restrict|_Atomic))?)*)\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTaggedSizeofTypeRegex = new(
        @"\bsizeof\s*\(\s*(?<type>(?:struct|enum|union)\s+[A-Za-z_]\w*)\s*(?:\s*\*(?:\s*(?:const|volatile|restrict|_Atomic))?)*\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTypedefAlignofTypeRegex = new(
        @"\b(?:_Alignof|alignof|__alignof__|__alignof)\s*\(\s*(?<type>(?:(?:const|volatile|restrict|_Atomic)\s+)*[A-Za-z_]\w*_t(?:\s*\*)*)\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTaggedAlignofTypeRegex = new(
        @"\b(?:_Alignof|alignof|__alignof__|__alignof)\s*\(\s*(?<type>(?:struct|enum|union)\s+[A-Za-z_]\w*)\s*(?:\*+\s*)?\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTypedefDeclarationTypeRegex = new(
        @"(?<![\w])(?<type>(?:(?:const|volatile|restrict|_Atomic)\s+)*[A-Za-z_]\w*_t\b)(?:\s*\*)*\s*(?:(?:const|volatile|restrict|_Atomic)\s+)*[A-Za-z_]\w*\s*(?=[=,;\[])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTaggedDeclarationTypeRegex = new(
        @"(?<![\w])(?<type>(?:struct|enum|union)\s+[A-Za-z_]\w*)\s*(?:\*+\s*)?(?:(?:const|volatile|restrict|_Atomic)\s+)*[A-Za-z_]\w*\s*(?=[=,;\[])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTypedefFunctionReturnTypeRegex = new(
        @"^\s*(?:(?:static|extern|inline|const|volatile|restrict|_Atomic)\s+)*(?<type>[A-Za-z_]\w*_t\b)(?:\s*\*)*\s*[A-Za-z_]\w*\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTaggedFunctionReturnTypeRegex = new(
        @"^\s*(?:(?:static|extern|inline|const|volatile|restrict|_Atomic)\s+)*(?<type>(?:struct|enum|union)\s+[A-Za-z_]\w*)\s*(?:\*+\s*)?[A-Za-z_]\w*\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTypedefParameterTypeRegex = new(
        @"(?:\(|,)\s*(?<type>(?:(?:const|volatile|restrict|_Atomic)\s+)*[A-Za-z_]\w*_t\b)(?:\s*\*)*\s*[A-Za-z_]\w*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTaggedParameterTypeRegex = new(
        @"(?:\(|,)\s*(?<type>(?:struct|enum|union)\s+[A-Za-z_]\w*)\s*(?:\*+\s*)?[A-Za-z_]\w*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTypedefCompoundLiteralTypeRegex = new(
        @"\(\s*(?<type>(?:(?:const|volatile|restrict|_Atomic)\s+)*[A-Za-z_]\w*_t\b)(?:\s*\*)*\s*\)\s*\{",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTaggedCompoundLiteralTypeRegex = new(
        @"\(\s*(?<type>(?:struct|enum|union)\s+[A-Za-z_]\w*)\s*(?:\*+\s*)?\)\s*\{",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTypedefTypeofTypeRegex = new(
        @"\b(?:typeof|__typeof__|__typeof)\s*\(\s*(?<type>(?:(?:const|volatile|restrict|_Atomic)\s+)*[A-Za-z_]\w*_t\b)(?:\s*\*)*\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTaggedTypeofTypeRegex = new(
        @"\b(?:typeof|__typeof__|__typeof)\s*\(\s*(?<type>(?:struct|enum|union)\s+[A-Za-z_]\w*)\s*(?:\*+\s*)?\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTypedefTypeofUnqualTypeRegex = new(
        @"\b(?:typeof_unqual|__typeof_unqual__|__typeof_unqual)\s*\(\s*(?<type>(?:(?:const|volatile|restrict|_Atomic)\s+)*[A-Za-z_]\w*_t\b)(?:\s*\*)*\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTaggedTypeofUnqualTypeRegex = new(
        @"\b(?:typeof_unqual|__typeof_unqual__|__typeof_unqual)\s*\(\s*(?<type>(?:struct|enum|union)\s+[A-Za-z_]\w*)\s*(?:\*+\s*)?\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTypedefGenericAssociationTypeRegex = new(
        @"(?:_Generic\s*\([^,;{}]*,|,)\s*(?<type>(?:(?:const|volatile|restrict|_Atomic)\s+)*[A-Za-z_]\w*_t\b)(?:\s*\*)*\s*:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTaggedGenericAssociationTypeRegex = new(
        @"(?:_Generic\s*\([^,;{}]*,|,)\s*(?<type>(?:struct|enum|union)\s+[A-Za-z_]\w*)\s*(?:\*+\s*)?:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTypedefAtomicTypeRegex = new(
        @"\b_Atomic\s*\(\s*(?<type>(?:(?:const|volatile|restrict)\s+)*[A-Za-z_]\w*_t\b)(?:\s*\*)*\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTaggedAtomicTypeRegex = new(
        @"\b_Atomic\s*\(\s*(?<type>(?:struct|enum|union)\s+[A-Za-z_]\w*)\s*(?:\*+\s*)?\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTypedefAlignasTypeRegex = new(
        @"\b(?:_Alignas|alignas)\s*\(\s*(?<type>(?:(?:const|volatile|restrict|_Atomic)\s+)*[A-Za-z_]\w*_t\b)(?:\s*\*)*\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTaggedAlignasTypeRegex = new(
        @"\b(?:_Alignas|alignas)\s*\(\s*(?<type>(?:struct|enum|union)\s+[A-Za-z_]\w*)\s*(?:\*+\s*)?\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTypedefFunctionPointerAliasTypeRegex = new(
        @"\btypedef\s+(?<type>(?:(?:const|volatile|restrict|_Atomic)\s+)*[A-Za-z_]\w*_t\b)(?:\s*\*)*\s*\(\s*\*\s*[A-Za-z_]\w*\s*\)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTaggedFunctionPointerAliasTypeRegex = new(
        @"\btypedef\s+(?<type>(?:struct|enum|union)\s+[A-Za-z_]\w*)\s*(?:\*+\s*)?\(\s*\*\s*[A-Za-z_]\w*\s*\)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTypedefFunctionPointerDeclarationTypeRegex = new(
        @"(?<![\w])(?<type>(?:(?:const|volatile|restrict|_Atomic)\s+)*[A-Za-z_]\w*_t\b)(?:\s*\*)*\s*\(\s*\*\s*[A-Za-z_]\w*\s*\)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTaggedFunctionPointerDeclarationTypeRegex = new(
        @"(?<![\w])(?<type>(?:struct|enum|union)\s+[A-Za-z_]\w*)\s*(?:\*+\s*)?\(\s*\*\s*[A-Za-z_]\w*\s*\)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTypedefPointerArrayDeclarationTypeRegex = new(
        @"(?<![\w])(?<type>(?:(?:const|volatile|restrict|_Atomic)\s+)*[A-Za-z_]\w*_t\b)(?:\s*\*)*\s*\(\s*\*\s*[A-Za-z_]\w*\s*\)\s*\[",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTaggedPointerArrayDeclarationTypeRegex = new(
        @"(?<![\w])(?<type>(?:struct|enum|union)\s+[A-Za-z_]\w*)\s*(?:\*+\s*)?\(\s*\*\s*[A-Za-z_]\w*\s*\)\s*\[",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTypedefOffsetofTypeRegex = new(
        @"\boffsetof\s*\(\s*(?<type>(?:(?:const|volatile|restrict|_Atomic)\s+)*[A-Za-z_]\w*_t\b)(?:\s*\*)*\s*,",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTaggedOffsetofTypeRegex = new(
        @"\boffsetof\s*\(\s*(?<type>(?:struct|enum|union)\s+[A-Za-z_]\w*)\s*(?:\*+\s*)?,",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTypedefVaArgTypeRegex = new(
        @"\bva_arg\s*\(\s*[^,;{}]+,\s*(?<type>(?:(?:const|volatile|restrict|_Atomic)\s+)*[A-Za-z_]\w*_t\b)(?:\s*\*)*\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CTaggedVaArgTypeRegex = new(
        @"\bva_arg\s*\(\s*[^,;{}]+,\s*(?<type>(?:struct|enum|union)\s+[A-Za-z_]\w*)\s*(?:\*+\s*)?\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppTypeOperandOperatorRegex = new(
        @"\b(?:sizeof|alignof)\s*\(\s*(?<type>(?:(?:const|volatile|typename|class|struct|enum)\s+)*(?:[A-Z_]\w*|[A-Za-z_]\w*\s*::\s*[A-Za-z_]\w*)(?:\s*<[^;{}]+>)?(?:\s*[*&])*)\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppTypeIdRegex = new(
        @"\btypeid\s*\(\s*(?<type>(?:(?:const|volatile|typename|class|struct|enum)\s+)*(?:[A-Z_]\w*|[A-Za-z_]\w*\s*::\s*[A-Za-z_]\w*)(?:\s*<[^;{}]+>)?(?:\s*[*&])*)\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppDecltypeBraceConstructionRegex = new(
        @"\bdecltype\s*\(\s*(?<type>(?:[A-Za-z_]\w*\s*::\s*)*[A-Z_]\w*(?:\s*<[^;{}()]+>)?)\s*\{",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppFactoryTemplateArgumentRegex = new(
        @"\b(?:std\s*::\s*)?(?:make_unique|make_shared|make_optional)\s*<(?<type>[^;{}<>]+(?:<[^;{}<>]+>)?[^;{}<>]*)>\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppTypeTraitTemplateArgumentRegex = new(
        @"\b(?:std\s*::\s*)?(?:is_same|is_base_of|is_convertible|is_constructible|is_assignable|is_invocable)(?:_v)?\s*<(?<type>[^;{}<>]+(?:<[^;{}<>]+>)?[^;{}<>]*)>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppBraceConstructionRegex = new(
        @"(?:=\s*|return\s+|co_return\s+|throw\s+)(?<type>(?:[A-Za-z_]\w*\s*::\s*)*[A-Z_]\w*(?:\s*<[^;{}]+>)?)\s*\{",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppQualifiedTemplateBraceConstructionRegex = new(
        @"(?:=\s*|return\s+|co_return\s+|throw\s+)(?:[A-Za-z_]\w*\s*::\s*)+[A-Za-z_]\w*\s*<(?<args>[^;{}]+)>\s*\{",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppUsingAliasTargetRegex = new(
        @"\b(?:template\s*<[^>]*>\s*)?using\s+[A-Za-z_]\w*\s*=\s*(?<type>[^;]+);",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppTypedefAliasTargetRegex = new(
        @"\btypedef\s+(?![^;]*\()(?<type>.+?)\s+[A-Za-z_]\w*\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppExplicitTemplateInstantiationRegex = new(
        @"\b(?:extern\s+)?template\s+(?:class|struct)\s+(?<type>[^;]+);",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppTemplateParameterDefaultTypeRegex = new(
        @"\b(?:typename|class)\s+[A-Za-z_]\w*\s*=\s*(?<type>(?:[A-Za-z_]\w*\s*::\s*)*[A-Za-z_]\w*(?:\s*<[^,>]+>)?(?:\s*[*&])?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppQualifiedMemberReceiverRegex = new(
        @"(?<![\w:])(?<type>(?:(?:[A-Za-z_]\w*)\s*::\s*)*[A-Z_]\w*)\s*::\s*[A-Za-z_]\w*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppPointerToMemberTypeRegex = new(
        @"(?<![\w:])(?<type>(?:(?:[A-Za-z_]\w*)\s*::\s*)*[A-Z_]\w*)\s*::\s*\*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppTrailingReturnTypeRegex = new(
        @"\)\s*->\s*(?<type>(?:(?:const|volatile|typename|class|struct|enum)\s+)*(?:[A-Z_]\w*|[A-Za-z_]\w*\s*::\s*[A-Za-z_]\w*)(?:\s*<[^;{}]+>)?(?:\s*[*&])*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppRequiresConceptTypeRegex = new(
        @"\brequires\s+(?<type>(?:(?:[A-Za-z_]\w*)\s*::\s*)*[A-Z_]\w*)\s*<",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppParenthesizedRequiresConceptTypeRegex = new(
        @"\brequires\s*\(\s*(?<type>(?:(?:[A-Za-z_]\w*)\s*::\s*)*[A-Z_]\w*)\s*<",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppQualifiedRequiresConceptConstraintRegex = new(
        @"\brequires\s*\(?\s*(?<concept>(?:(?:[A-Za-z_]\w*)\s*::\s*)+[A-Za-z_]\w*)\s*<(?<args>[^;{}<>]+(?:<[^;{}<>]+>)?[^;{}<>]*)>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppConceptExpressionTypeRegex = new(
        @"(?:=|&&|\|\|)\s*(?<type>(?:(?:[A-Za-z_]\w*)\s*::\s*)*[A-Z_]\w*)\s*<",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppCompoundRequirementConceptRegex = new(
        @"->\s*(?<concept>(?:(?:[A-Za-z_]\w*)\s*::\s*)*[A-Za-z_]\w*)\s*<(?<args>[^;{}<>]+(?:<[^;{}<>]+>)?[^;{}<>]*)>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppFriendTypeRegex = new(
        @"\bfriend\s+(?:class|struct|enum(?:\s+class)?)\s+(?<type>(?:[A-Za-z_]\w*\s*::\s*)*[A-Za-z_]\w*)\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppDynamicExceptionSpecRegex = new(
        @"\bthrow\s*\(\s*(?<type>(?:(?:[A-Za-z_]\w*\s*::\s*)*[A-Z_]\w*(?:\s*[*&])?(?:\s*,\s*)?)+)\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppDeclarationTypeRegex = new(
        @"(?<![\w:])(?<type>(?:(?:const|volatile|static|inline|constexpr|typename|class|struct|enum)\s+)*(?:[A-Z_]\w*|[A-Za-z_]\w*\s*::\s*[A-Za-z_]\w*)(?:\s*<[^;{}]+>)?(?:\s*[*&])*)\s+(?<name>[A-Za-z_]\w*)\s*(?=[,;)=])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GoImportRegex = new(
        @"^\s*import\s+(?:\(\s*)?(?:(?<alias>[A-Za-z_]\w*|\.)\s+)?""(?<name>[^""]+)""(?:\s*\))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoImportBlockStartRegex = new(
        @"^\s*import\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoImportBlockEntryRegex = new(
        @"^\s*(?:(?<alias>[A-Za-z_]\w*|\.)\s+)?""(?<name>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoVarTypeRegex = new(
        @"\b(?:var|const)\s+[A-Za-z_]\w*\s+(?<type>[\*\[\]\w.]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoFieldTypeRegex = new(
        @"^\s*(?!(?:package|import|func|type|var|const|return|defer|go|break|continue|goto|if|for|switch|select|case|default|else)\b)[A-Za-z_]\w*\s+(?<type>[\*\[\]\w.]+)(?:\s|`|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoTypeAliasRegex = new(
        @"^\s*type\s+[A-Za-z_]\w*(?:\[[^\]]+\])?\s+=?\s*(?<type>[\*\[\]\w.]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoFuncRegex = new(
        @"^\s*func\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoCompositeLiteralRegex = new(
        @"(?<!\btype\s)(?<name>[A-Z]\w*)\s*\{",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoBuiltinTypeArgumentRegex = new(
        @"\b(?:make|new)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoTypeAssertionRegex = new(
        @"\.\s*\(\s*(?<type>[^()\r\n]+?)\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoTypeSwitchCaseRegex = new(
        @"^\s*case\s+(?<types>.+?)\s*:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoFunctionLiteralRegex = new(
        @"\bfunc\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoBranchLabelRegex = new(
        @"\b(?:goto|break|continue)\s+(?<name>[A-Za-z_]\w*)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DartCtorRegex = new(
        @"\b(?:new|const)\s+(?<name>[A-Z]\w*(?:\.[A-Za-z_]\w*)?)\s*(?:<[^>]+>)?\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DartVariableTypeRegex = new(
        @"^\s*(?:(?:final|late|const)\s+)*(?<type>[A-Z]\w*(?:\s*<[^;=]+>)?)\s+[A-Za-z_]\w*\s*(?:=|;)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DartFunctionSignatureRegex = new(
        @"^\s*(?:(?:external|static|abstract)\s+)*(?<return>[A-Z]\w*(?:\s*<[^;{}()]+>)?)\s+[A-Za-z_]\w*\s*\((?<params>[^)]*)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DartParameterTypeRegex = new(
        @"(?:^|,)\s*(?:(?:required|covariant|final)\s+)*(?<type>[A-Z]\w*(?:\s*<[^,)=]+>)?)\s+[A-Za-z_]\w*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex VbTypeKeywordRegex = new(
        @"\b(?:As|New|Inherits|Implements|Of)\s+(?<type>(?:Global\.)?[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex VbAddressOfRegex = new(
        @"\bAddressOf\s+(?<name>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex VbHandlesRegex = new(
        @"\bHandles\s+(?:[A-Za-z_]\w*\.)?(?<name>[A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex FortranUseRegex = new(
        @"^\s*use(?:\s*,\s*(?:intrinsic|non_intrinsic))?(?:\s*::)?\s+(?<name>[A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FortranTypeRegex = new(
        @"\b(?:type|class)\s*\(\s*(?<type>[A-Za-z_]\w*)\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FortranCallRegex = new(
        @"^\s*call\s+(?<name>[A-Za-z_]\w*)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PascalUsesRegex = new(
        @"^\s*uses\s+(?<list>.+?)(?:;|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PascalTypeAfterColonRegex = new(
        @":\s*(?<type>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PascalClassBaseRegex = new(
        @"=\s*(?:class|interface|object)\s*\((?<bases>[^)]+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PascalBareCallRegex = new(
        @"^\s*(?<name>[A-Za-z_]\w*)\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ObjCMessageRegex = new(
        @"\[\s*(?<receiver>[A-Za-z_]\w*)\s+(?<name>[A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ObjCInterfaceBaseRegex = new(
        @"^\s*@(?:interface|implementation)\s+[A-Za-z_]\w+(?:\s*\([^)]+\))?\s*:\s*(?<type>[A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ObjCProtocolListRegex = new(
        @"<(?<list>[A-Za-z_]\w*(?:\s*,\s*[A-Za-z_]\w*)*)>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ObjCDeclTypeRegex = new(
        @"(?<type>[A-Z]\w*)\s*\*+\s*[A-Za-z_]\w*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ObjCSelectorRegex = new(
        @"@selector\s*\(\s*(?<name>[A-Za-z_]\w*:?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HaskellSignatureRegex = new(
        @"^\s*[a-z_]\w*\s*::\s*(?<types>.+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HaskellSpaceCallRegex = new(
        @"^\s*(?<name>[a-z_]\w*)\s+(?=(?:[A-Za-z_(]))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HaskellDefinitionRegex = new(
        @"^\s*(?<name>[a-z_]\w*)\b.*=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ElixirImportRegex = new(
        @"^\s*(?:alias|import|require|use)\s+(?<name>[A-Z]\w*(?:\.[A-Z]\w*)*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ElixirBehaviourRegex = new(
        @"^\s*@(?:behaviour|impl)\s+(?<name>[A-Z]\w*(?:\.[A-Z]\w*)*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ElixirParenlessCallRegex = new(
        @"(?<![\w])(?<name>[a-z_]\w*[?!]?)\s+(?=(?:[A-Za-z_:@\[""']))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LuaRequireRegex = new(
        @"\brequire\s*\(?\s*[""'](?<name>[^""']+)[""']",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LuaCommandCallRegex = new(
        @"^\s*(?<name>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)?)\s+(?=[""'{A-Za-z_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SmalltalkClassDeclarationRegex = new(
        @"^\s*(?:(?:[A-Za-z_]\w*)\s+subclass:|Class\s+named:|Object\s+subclass:)\s*#",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SmalltalkMessageSendRegex = new(
        @"(?<![#\w])(?<receiver>[A-Za-z_]\w*)\s+(?<selector>[a-z]\w*:?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SmalltalkMethodDefinitionRegex = new(
        @">>\s*(?<name>[A-Za-z_]\w*:?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RazorComponentTagRegex = new(
        @"<(?<name>[A-Z][A-Za-z0-9_]*(?:\.[A-Za-z_]\w*)?)(?=[\s>/])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RazorDirectiveTypeRegex = new(
        @"^\s*@(?:inherits|implements|model)\s+(?<type>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RazorInjectRegex = new(
        @"^\s*@inject\s+(?<type>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s+[A-Za-z_]\w*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RazorEventHandlerRegex = new(
        @"@on[A-Za-z_]\w*\s*=\s*""(?<name>[A-Za-z_]\w*)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static void EmitTypePositionReferences(
        string language,
        string preparedLine,
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        SymbolRecord? container,
        bool isGoImportBlockLine = false)
    {
        switch (language)
        {
            case "c":
            case "cpp":
                EmitCppTypeReferences(language, preparedLine, originalLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
                break;
            case "go":
                EmitGoTypeReferences(preparedLine, originalLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn, isGoImportBlockLine);
                break;
            case "dart":
                EmitDartTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
                break;
            case "vb":
                EmitVbTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
                break;
            case "fortran":
                EmitFortranTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn, container);
                break;
            case "pascal":
                EmitPascalTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn, container);
                break;
            case "objc":
                EmitObjCTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn, container);
                break;
            case "haskell":
                EmitHaskellTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, container);
                break;
            case "elixir":
                EmitElixirTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, container);
                break;
            case "lua":
                EmitLuaTypeReferences(originalLine, references, seen, fileId, context, lineNumber, container);
                break;
        }
    }

    public static void EmitAdditionalCallReferences(
        string language,
        string preparedLine,
        string originalLine,
        Action<string, int> addCallLikeReference,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        IReadOnlySet<string>? definitionNames)
    {
        switch (language)
        {
            case "fortran":
                EmitFortranCallReferences(preparedLine, addCallLikeReference);
                break;
            case "pascal":
                EmitPascalCallReferences(preparedLine, addCallLikeReference, definitionNames);
                break;
            case "objc":
                EmitObjCMessageReferences(preparedLine, addCallLikeReference, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
                break;
            case "haskell":
                EmitHaskellSpaceCallReferences(preparedLine, addCallLikeReference, definitionNames);
                break;
            case "elixir":
                EmitElixirParenlessCallReferences(preparedLine, addCallLikeReference, definitionNames);
                break;
            case "lua":
                EmitLuaCommandCallReferences(preparedLine, addCallLikeReference, definitionNames);
                break;
            case "smalltalk":
                EmitSmalltalkMessageReferences(preparedLine, addCallLikeReference, definitionNames);
                break;
        }
    }

    public static void EmitRazorReferences(
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        IReadOnlySet<string>? definitionNames)
    {
        foreach (Match match in RazorComponentTagRegex.Matches(originalLine))
        {
            var group = match.Groups["name"];
            var rawName = group.Value;
            var name = LastQualifiedSegment(rawName);
            if (definitionNames?.Contains(name) == true)
                continue;
            var nameOffset = rawName.LastIndexOf(name, StringComparison.Ordinal);
            var nameIndex = group.Index + Math.Max(0, nameOffset);

            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                name,
                nameIndex,
                "call",
                context,
                lineNumber,
                resolveContainerForColumn(nameIndex));
        }

        foreach (var match in EnumerateMatches(RazorDirectiveTypeRegex, originalLine).Concat(EnumerateMatches(RazorInjectRegex, originalLine)))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(
                references,
                seen,
                fileId,
                group.Value,
                group.Index,
                context,
                lineNumber,
                resolveContainerForColumn(group.Index),
                "csharp");
        }

        foreach (Match match in RazorEventHandlerRegex.Matches(originalLine))
        {
            var name = match.Groups["name"].Value;
            if (definitionNames?.Contains(name) == true)
                continue;

            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                name,
                match.Groups["name"].Index,
                "call",
                context,
                lineNumber,
                resolveContainerForColumn(match.Groups["name"].Index));
        }
    }

    public static bool[] BuildGoImportBlockLineMap(IReadOnlyList<string> originalLines)
    {
        var result = new bool[originalLines.Count];
        var inImportBlock = false;
        var inBlockComment = false;

        for (var i = 0; i < originalLines.Count; i++)
        {
            var line = originalLines[i];
            var codeLine = StripGoComments(line, ref inBlockComment);
            var trimmed = codeLine.Trim();
            if (!inImportBlock)
            {
                if (GoImportBlockStartRegex.IsMatch(codeLine))
                    inImportBlock = !trimmed.Contains(')');
                continue;
            }

            if (trimmed.StartsWith(')'))
            {
                inImportBlock = false;
                continue;
            }

            result[i] = GoImportBlockEntryRegex.IsMatch(codeLine);
            if (trimmed.Contains(')'))
                inImportBlock = false;
        }

        return result;
    }

    private static string StripGoComments(string line, ref bool inBlockComment)
    {
        var chars = line.ToCharArray();
        for (var i = 0; i < line.Length; i++)
        {
            if (inBlockComment)
            {
                chars[i] = ' ';
                if (line[i] == '*' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    chars[++i] = ' ';
                    inBlockComment = false;
                }
                continue;
            }

            if (line[i] is '"' or '`')
            {
                i = SkipGoStringLiteral(line, i);
                continue;
            }

            if (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                for (; i < chars.Length; i++)
                    chars[i] = ' ';
                break;
            }

            if (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '*')
            {
                chars[i++] = ' ';
                chars[i] = ' ';
                inBlockComment = true;
            }
        }

        return new string(chars);
    }

    private static int SkipGoStringLiteral(string line, int start)
    {
        var quote = line[start];
        var i = start + 1;
        while (i < line.Length)
        {
            if (quote == '"' && line[i] == '\\' && i + 1 < line.Length)
            {
                i += 2;
                continue;
            }

            if (line[i] == quote)
                return i;
            i++;
        }

        return line.Length;
    }

    public static string[] MaskLuaLongCommentAndStringLines(IReadOnlyList<string> originalLines)
    {
        var result = new string[originalLines.Count];
        var longTextEqualsCount = -1;

        for (var lineIndex = 0; lineIndex < originalLines.Count; lineIndex++)
        {
            var line = originalLines[lineIndex];
            var chars = line.ToCharArray();
            for (var cursor = 0; cursor < chars.Length; cursor++)
            {
                if (longTextEqualsCount >= 0)
                {
                    if (TryGetLuaLongBracketClose(line, cursor, longTextEqualsCount, out var closeLength))
                    {
                        MaskRange(chars, cursor, cursor + closeLength);
                        cursor += closeLength - 1;
                        longTextEqualsCount = -1;
                        continue;
                    }

                    chars[cursor] = ' ';
                    continue;
                }

                if (chars[cursor] is '"' or '\'')
                {
                    cursor = SkipQuotedLiteral(line, cursor);
                    continue;
                }

                if (chars[cursor] == '-'
                    && cursor + 2 < chars.Length
                    && chars[cursor + 1] == '-'
                    && TryGetLuaLongBracketOpen(line, cursor + 2, out var commentEqualsCount, out var commentOpenLength))
                {
                    MaskRange(chars, cursor, cursor + 2 + commentOpenLength);
                    cursor += 1 + commentOpenLength;
                    longTextEqualsCount = commentEqualsCount;
                    continue;
                }

                if (chars[cursor] == '-' && cursor + 1 < chars.Length && chars[cursor + 1] == '-')
                    break;

                if (TryGetLuaLongBracketOpen(line, cursor, out var stringEqualsCount, out var stringOpenLength))
                {
                    MaskRange(chars, cursor, cursor + stringOpenLength);
                    cursor += stringOpenLength - 1;
                    longTextEqualsCount = stringEqualsCount;
                }
            }

            result[lineIndex] = new string(chars);
        }

        return result;
    }

    private static bool TryGetLuaLongBracketOpen(string line, int start, out int equalsCount, out int length)
    {
        equalsCount = 0;
        length = 0;
        if (start < 0 || start >= line.Length || line[start] != '[')
            return false;

        var cursor = start + 1;
        while (cursor < line.Length && line[cursor] == '=')
        {
            equalsCount++;
            cursor++;
        }

        if (cursor >= line.Length || line[cursor] != '[')
            return false;

        length = cursor - start + 1;
        return true;
    }

    private static bool TryGetLuaLongBracketClose(string line, int start, int equalsCount, out int length)
    {
        length = 0;
        if (start < 0 || start >= line.Length || line[start] != ']')
            return false;

        var cursor = start + 1;
        for (var i = 0; i < equalsCount; i++)
        {
            if (cursor >= line.Length || line[cursor] != '=')
                return false;
            cursor++;
        }

        if (cursor >= line.Length || line[cursor] != ']')
            return false;

        length = cursor - start + 1;
        return true;
    }

    public static string[] MaskRazorCommentLines(IReadOnlyList<string> originalLines)
    {
        var result = new string[originalLines.Count];
        var inRazorComment = false;
        var inHtmlComment = false;
        var inCodeBlock = false;
        var codeDepth = 0;
        var inCSharpBlockComment = false;
        var razorControlDepth = 0;
        var inRazorControlBlockComment = false;
        var pendingRazorControlBlock = false;

        for (var lineIndex = 0; lineIndex < originalLines.Count; lineIndex++)
        {
            var line = originalLines[lineIndex];
            var chars = line.ToCharArray();
            var cursor = 0;
            while (cursor < line.Length)
            {
                if (inRazorComment)
                {
                    var close = line.IndexOf("*@", cursor, StringComparison.Ordinal);
                    var end = close < 0 ? line.Length : close + 2;
                    MaskRange(chars, cursor, end);
                    inRazorComment = close < 0;
                    cursor = end;
                    continue;
                }

                if (inHtmlComment)
                {
                    var close = line.IndexOf("-->", cursor, StringComparison.Ordinal);
                    var end = close < 0 ? line.Length : close + 3;
                    MaskRange(chars, cursor, end);
                    inHtmlComment = close < 0;
                    cursor = end;
                    continue;
                }

                if (line.AsSpan(cursor).StartsWith("@*", StringComparison.Ordinal))
                {
                    inRazorComment = true;
                    continue;
                }

                if (line.AsSpan(cursor).StartsWith("<!--", StringComparison.Ordinal))
                {
                    inHtmlComment = true;
                    continue;
                }

                cursor++;
            }

            var codeScanStart = 0;
            if (!inCodeBlock)
            {
                codeScanStart = IndexOfRazorCodeDirective(chars);
                if (codeScanStart >= 0)
                    inCodeBlock = true;
                else
                {
                    codeScanStart = IndexOfRazorExplicitCodeBlock(chars);
                    if (codeScanStart >= 0)
                        inCodeBlock = true;
                }
            }

            if (inCodeBlock)
                MaskCSharpStringsAndCommentsInRazorCode(chars, Math.Max(0, codeScanStart), ref codeDepth, ref inCodeBlock, ref inCSharpBlockComment);
            else
            {
                var controlStart = IndexOfRazorControlDirective(chars);
                if (controlStart >= 0)
                {
                    var delta = MaskRazorControlCodeLine(chars, controlStart, ref inRazorControlBlockComment);
                    razorControlDepth = Math.Max(0, razorControlDepth + delta);
                    pendingRazorControlBlock = delta <= 0;
                }
                else if (IndexOfRazorBareControlContinuation(chars) is var continuationStart && continuationStart >= 0)
                {
                    var delta = MaskRazorControlCodeLine(chars, continuationStart, ref inRazorControlBlockComment);
                    razorControlDepth = Math.Max(0, razorControlDepth + delta);
                    pendingRazorControlBlock = delta <= 0;
                }
                else if (pendingRazorControlBlock && IsRazorCodeLineInsideControl(chars))
                {
                    var delta = MaskRazorControlCodeLine(chars, FirstNonWhitespaceIndex(chars), ref inRazorControlBlockComment);
                    razorControlDepth = Math.Max(0, razorControlDepth + delta);
                    if (delta != 0)
                        pendingRazorControlBlock = false;
                }
                else if (razorControlDepth > 0 && IsRazorCodeLineInsideControl(chars))
                {
                    razorControlDepth = Math.Max(
                        0,
                        razorControlDepth + MaskRazorControlCodeLine(chars, FirstNonWhitespaceIndex(chars), ref inRazorControlBlockComment));
                }
            }

            result[lineIndex] = new string(chars);
        }

        return result;
    }

    private static int IndexOfRazorCodeDirective(char[] chars)
    {
        var line = new string(chars);
        foreach (var directive in new[] { "@code", "@functions" })
        {
            var index = line.IndexOf(directive, StringComparison.Ordinal);
            if (index < 0)
                continue;
            var beforeOk = index == 0 || char.IsWhiteSpace(line[index - 1]);
            var afterIndex = index + directive.Length;
            var afterOk = afterIndex == line.Length || char.IsWhiteSpace(line[afterIndex]) || line[afterIndex] == '{';
            if (beforeOk && afterOk)
                return index;
        }

        return -1;
    }

    private static int IndexOfRazorBareControlContinuation(char[] chars)
    {
        var index = FirstNonWhitespaceIndex(chars);
        if (index < 0)
            return -1;

        var line = new string(chars);
        foreach (var keyword in new[] { "else", "catch", "finally" })
        {
            if (line.AsSpan(index).StartsWith(keyword, StringComparison.Ordinal)
                && (index + keyword.Length == line.Length || !IsSimpleIdentifierPart(line[index + keyword.Length])))
            {
                return index;
            }
        }

        return -1;
    }

    private static int IndexOfRazorExplicitCodeBlock(char[] chars)
    {
        var line = new string(chars);
        var index = line.IndexOf("@{", StringComparison.Ordinal);
        return index >= 0 ? index : -1;
    }

    private static int IndexOfRazorControlDirective(char[] chars)
    {
        var line = new string(chars);
        foreach (var directive in new[] { "@if", "@foreach", "@for", "@while", "@switch", "@using", "@lock", "@try", "@catch", "@finally", "@do" })
        {
            for (var index = line.IndexOf(directive, StringComparison.Ordinal);
                 index >= 0;
                 index = line.IndexOf(directive, index + directive.Length, StringComparison.Ordinal))
            {
                var beforeOk = index == 0 || char.IsWhiteSpace(line[index - 1]) || line[index - 1] == '}';
                var afterIndex = index + directive.Length;
                var afterOk = afterIndex == line.Length || !IsSimpleIdentifierPart(line[afterIndex]);
                if (beforeOk && afterOk)
                    return index;
            }
        }

        return -1;
    }

    private static bool IsRazorCodeLineInsideControl(char[] chars)
    {
        var index = FirstNonWhitespaceIndex(chars);
        if (index < 0)
            return false;

        return !LooksLikeRazorMarkupStart(new string(chars), index);
    }

    private static int FirstNonWhitespaceIndex(char[] chars)
    {
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsWhiteSpace(chars[i]))
                return i;
        }

        return -1;
    }

    private static bool LooksLikeRazorMarkupStart(string line, int index)
    {
        while (index < line.Length && char.IsWhiteSpace(line[index]))
            index++;
        if (index >= line.Length)
            return false;
        if (line[index] == '<')
            return true;

        return line[index] == '@'
            && index + 1 < line.Length
            && line[index + 1] is ':' or '<';
    }

    private static int MaskRazorControlCodeLine(char[] chars, int start, ref bool inBlockComment)
    {
        var line = new string(chars);
        var firstOpenBrace = -1;
        var delta = CountCSharpBraceDelta(line, start, ref inBlockComment, ref firstOpenBrace);
        if (firstOpenBrace >= 0 && LooksLikeRazorMarkupStart(line, firstOpenBrace + 1))
        {
            MaskRange(chars, start, firstOpenBrace + 1);
            return delta;
        }

        MaskRange(chars, start, chars.Length);
        return delta;
    }

    private static int CountCSharpBraceDelta(string line, int start, ref bool inBlockComment, ref int firstOpenBrace)
    {
        var delta = 0;
        for (var cursor = Math.Max(0, start); cursor < line.Length; cursor++)
        {
            if (inBlockComment)
            {
                if (line[cursor] == '*' && cursor + 1 < line.Length && line[cursor + 1] == '/')
                {
                    inBlockComment = false;
                    cursor++;
                }

                continue;
            }

            if (line[cursor] == '/' && cursor + 1 < line.Length && line[cursor + 1] == '/')
                break;

            if (line[cursor] == '/' && cursor + 1 < line.Length && line[cursor + 1] == '*')
            {
                inBlockComment = true;
                cursor++;
                continue;
            }

            if (line[cursor] is '"' or '\'')
            {
                var quote = line[cursor++];
                while (cursor < line.Length)
                {
                    if (line[cursor] == '\\' && cursor + 1 < line.Length)
                    {
                        cursor += 2;
                        continue;
                    }

                    if (line[cursor] == quote)
                        break;
                    cursor++;
                }

                continue;
            }

            if (line[cursor] == '{')
            {
                if (firstOpenBrace < 0)
                    firstOpenBrace = cursor;
                delta++;
            }
            else if (line[cursor] == '}')
            {
                delta--;
            }
        }

        return delta;
    }

    private static void MaskCSharpStringsAndCommentsInRazorCode(
        char[] chars,
        int start,
        ref int codeDepth,
        ref bool inCodeBlock,
        ref bool inBlockComment)
    {
        for (var cursor = start; cursor < chars.Length; cursor++)
        {
            if (inBlockComment)
            {
                if (chars[cursor] == '*' && cursor + 1 < chars.Length && chars[cursor + 1] == '/')
                {
                    chars[cursor++] = ' ';
                    chars[cursor] = ' ';
                    inBlockComment = false;
                    continue;
                }

                chars[cursor] = ' ';
                continue;
            }

            if (chars[cursor] == '/' && cursor + 1 < chars.Length && chars[cursor + 1] == '/')
            {
                MaskRange(chars, cursor, chars.Length);
                break;
            }

            if (chars[cursor] == '/' && cursor + 1 < chars.Length && chars[cursor + 1] == '*')
            {
                chars[cursor++] = ' ';
                chars[cursor] = ' ';
                inBlockComment = true;
                continue;
            }

            if (chars[cursor] is '"' or '\'')
            {
                var quote = chars[cursor];
                chars[cursor++] = ' ';
                while (cursor < chars.Length)
                {
                    if (chars[cursor] == '\\' && cursor + 1 < chars.Length)
                    {
                        chars[cursor++] = ' ';
                        chars[cursor] = ' ';
                        cursor++;
                        continue;
                    }

                    var closes = chars[cursor] == quote;
                    chars[cursor++] = ' ';
                    if (closes)
                        break;
                }
                cursor--;
                continue;
            }

            if (chars[cursor] == '{')
            {
                codeDepth++;
                chars[cursor] = ' ';
                continue;
            }

            if (chars[cursor] == '}' && codeDepth > 0)
            {
                codeDepth--;
                chars[cursor] = ' ';
                if (codeDepth == 0)
                    inCodeBlock = false;
                continue;
            }

            chars[cursor] = ' ';
        }
    }

    private static void EmitCppTypeReferences(
        string language,
        string preparedLine,
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var includeMatch = !string.IsNullOrWhiteSpace(preparedLine)
            ? CppIncludeRegex.Match(originalLine)
            : Match.Empty;
        if (includeMatch.Success)
        {
            var group = includeMatch.Groups["name"];
            ReferenceExtractor.AddReference(references, seen, fileId, group.Value, group.Index, "type_reference", context, lineNumber, resolveContainerForColumn(group.Index));
        }

        var baseMatch = CppBaseListRegex.Match(preparedLine);
        if (baseMatch.Success)
        {
            var group = baseMatch.Groups["bases"];
            foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(group.Value))
            {
                var expression = StripCppAccessPrefix(group.Value.Substring(segmentStart, segmentLength));
                if (expression.Length == 0)
                    continue;

                var absoluteStart = group.Index + segmentStart + group.Value.Substring(segmentStart, segmentLength).IndexOf(expression, StringComparison.Ordinal);
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, expression, absoluteStart, context, lineNumber, resolveContainerForColumn(absoluteStart), language);
            }
        }

        foreach (Match match in CppNewTypeRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            var typeName = LastCppQualifiedSegment(group.Value);
            var typeStart = group.Index + group.Value.LastIndexOf(typeName, StringComparison.Ordinal);
            ReferenceExtractor.AddReference(references, seen, fileId, typeName, typeStart, "instantiate", context, lineNumber, resolveContainerForColumn(typeStart));
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppNamedCastTypeRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppCStyleCastTypeRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        if (language == "c")
        {
            foreach (Match match in CTypedefCastTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTypedefSizeofTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTaggedSizeofTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTypedefAlignofTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTaggedAlignofTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTypedefDeclarationTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTaggedDeclarationTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTypedefFunctionReturnTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTaggedFunctionReturnTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTypedefParameterTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTaggedParameterTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTypedefCompoundLiteralTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTaggedCompoundLiteralTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTypedefTypeofTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTaggedTypeofTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTypedefTypeofUnqualTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTaggedTypeofUnqualTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTypedefGenericAssociationTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTaggedGenericAssociationTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTypedefAtomicTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTaggedAtomicTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTypedefAlignasTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTaggedAlignasTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTypedefFunctionPointerAliasTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTaggedFunctionPointerAliasTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTypedefFunctionPointerDeclarationTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTaggedFunctionPointerDeclarationTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTypedefPointerArrayDeclarationTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTaggedPointerArrayDeclarationTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTypedefOffsetofTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTaggedOffsetofTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTypedefVaArgTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CTaggedVaArgTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }
        }

        foreach (Match match in CppTypeOperandOperatorRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppTypeIdRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppDecltypeBraceConstructionRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppFactoryTemplateArgumentRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppTypeTraitTemplateArgumentRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppBraceConstructionRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            var typeName = LastCppQualifiedSegment(group.Value);
            var typeStart = group.Index + group.Value.LastIndexOf(typeName, StringComparison.Ordinal);
            ReferenceExtractor.AddReference(references, seen, fileId, typeName, typeStart, "instantiate", context, lineNumber, resolveContainerForColumn(typeStart));
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppQualifiedTemplateBraceConstructionRegex.Matches(preparedLine))
        {
            var group = match.Groups["args"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppUsingAliasTargetRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppTypedefAliasTargetRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppExplicitTemplateInstantiationRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppTemplateParameterDefaultTypeRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppQualifiedMemberReceiverRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppPointerToMemberTypeRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppTrailingReturnTypeRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        if (preparedLine.Contains("requires", StringComparison.Ordinal) || preparedLine.Contains("concept", StringComparison.Ordinal))
        {
            foreach (Match match in CppRequiresConceptTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CppParenthesizedRequiresConceptTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }

            foreach (Match match in CppQualifiedRequiresConceptConstraintRegex.Matches(preparedLine))
            {
                var conceptGroup = match.Groups["concept"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, conceptGroup.Value, conceptGroup.Index, context, lineNumber, resolveContainerForColumn(conceptGroup.Index), language);

                var argsGroup = match.Groups["args"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, argsGroup.Value, argsGroup.Index, context, lineNumber, resolveContainerForColumn(argsGroup.Index), language);
            }

            foreach (Match match in CppConceptExpressionTypeRegex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
            }
        }

        foreach (Match match in CppCompoundRequirementConceptRegex.Matches(preparedLine))
        {
            var conceptGroup = match.Groups["concept"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, conceptGroup.Value, conceptGroup.Index, context, lineNumber, resolveContainerForColumn(conceptGroup.Index), language);

            var argsGroup = match.Groups["args"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, argsGroup.Value, argsGroup.Index, context, lineNumber, resolveContainerForColumn(argsGroup.Index), language);
        }

        foreach (Match match in CppFriendTypeRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppDynamicExceptionSpecRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppDeclarationTypeRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            var expression = StripCppAccessPrefix(group.Value);
            if (expression.Length == 0)
                continue;

            var start = group.Index + group.Value.IndexOf(expression, StringComparison.Ordinal);
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, expression, start, context, lineNumber, resolveContainerForColumn(start), language);
        }
    }

    private static void EmitGoTypeReferences(
        string preparedLine,
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        bool isImportBlockLine)
    {
        var importMatch = !string.IsNullOrWhiteSpace(preparedLine)
            ? isImportBlockLine
                ? GoImportBlockEntryRegex.Match(originalLine)
                : GoImportRegex.Match(originalLine)
            : Match.Empty;
        if (importMatch.Success)
        {
            var group = importMatch.Groups["name"];
            var aliasGroup = importMatch.Groups["alias"];
            if (aliasGroup.Success && aliasGroup.Value is not "." and not "_")
            {
                ReferenceExtractor.AddReference(references, seen, fileId, aliasGroup.Value, aliasGroup.Index, "type_reference", context, lineNumber, resolveContainerForColumn(aliasGroup.Index));
            }
            else
            {
                var packageName = LastPathSegment(group.Value);
                var packageOffset = group.Value.LastIndexOf(packageName, StringComparison.Ordinal);
                ReferenceExtractor.AddReference(references, seen, fileId, packageName, group.Index + Math.Max(0, packageOffset), "type_reference", context, lineNumber, resolveContainerForColumn(group.Index));
            }
        }

        EmitGoTypeDeclarationParameterConstraints(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoTypeSpecTargetReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoInterfaceTypeSetTermReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoStandaloneTypeSetTermReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoSingleNameValueDeclarationTypes(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoMultiNameValueDeclarationTypes(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoMultiNameFieldDeclarationTypes(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoSingleNameFieldDeclarationTypes(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoEmbeddedFieldType(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoBuiltinTypeArgumentReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoChannelElementTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoTypeAssertionReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoTypeSwitchCaseReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoFunctionLiteralSignatureTypes(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoFunctionTypeSignatureTypes(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoInlineStructFieldTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoInlineInterfaceMemberTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoGenericCompositeLiteralReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoArraySliceCompositeLiteralTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoMapCompositeLiteralTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoCompositeTypeConversionReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoParenthesizedTypeConversionReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoMethodExpressionReceiverTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoGenericInstantiationTypeArgumentReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoGenericCallTypeArgumentReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);

        foreach (var regex in new[] { GoVarTypeRegex, GoFieldTypeRegex, GoTypeAliasRegex })
        {
            foreach (Match match in regex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                EmitGoTypeExpression(group.Value, group.Index, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
            }
        }

        if (GoFuncRegex.IsMatch(preparedLine))
            EmitGoFunctionSignatureTypes(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        else
            EmitGoInterfaceMethodSignatureTypes(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);

        foreach (Match match in GoCompositeLiteralRegex.Matches(preparedLine))
        {
            var group = match.Groups["name"];
            if (!IsGoCompositeLiteralContext(preparedLine, group.Index, group.Value.Length))
                continue;

            ReferenceExtractor.AddReference(references, seen, fileId, group.Value, group.Index, "instantiate", context, lineNumber, resolveContainerForColumn(group.Index));
        }
    }

    private static void EmitGoTypeDeclarationParameterConstraints(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var cursor = SkipWhitespace(line, 0);
        if (StartsWithKeyword(line, cursor, "type"))
            cursor = SkipWhitespace(line, cursor + "type".Length);

        if (cursor >= line.Length || !IsIdentifierStart(line[cursor]))
            return;

        cursor++;
        while (cursor < line.Length && IsSimpleIdentifierPart(line[cursor]))
            cursor++;

        cursor = SkipWhitespace(line, cursor);
        if (cursor >= line.Length || line[cursor] != '[')
            return;

        var close = ReferenceExtractor.FindMatchingChar(line, cursor, '[', ']');
        if (close < 0)
            return;

        var afterClose = SkipWhitespace(line, close + 1);
        if (afterClose >= line.Length)
            return;
        if (line[afterClose] == '=')
            afterClose = SkipWhitespace(line, afterClose + 1);
        if (afterClose >= line.Length || !IsGoTypeDeclarationBodyStart(line, afterClose))
            return;

        EmitGoTypeParameterConstraints(line, cursor, close + 1, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitGoTypeSpecTargetReferences(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var cursor = SkipWhitespace(line, 0);
        if (!StartsWithKeyword(line, cursor, "type"))
            return;

        cursor = SkipWhitespace(line, cursor + "type".Length);
        if (cursor >= line.Length || !IsIdentifierStart(line[cursor]))
            return;

        cursor++;
        while (cursor < line.Length && IsSimpleIdentifierPart(line[cursor]))
            cursor++;

        cursor = SkipWhitespace(line, cursor);
        if (cursor < line.Length && line[cursor] == '[')
        {
            var close = ReferenceExtractor.FindMatchingChar(line, cursor, '[', ']');
            if (close < 0)
                return;
            cursor = SkipWhitespace(line, close + 1);
        }

        if (cursor < line.Length && line[cursor] == '=')
            cursor = SkipWhitespace(line, cursor + 1);

        if (cursor >= line.Length
            || StartsWithKeyword(line, cursor, "struct")
            || StartsWithKeyword(line, cursor, "interface")
            || !IsGoTypeExpressionStart(line, cursor))
        {
            return;
        }

        var typeEnd = FindGoInlineTypeExpressionEnd(line, cursor);
        if (typeEnd <= cursor)
            return;

        EmitGoTypeExpression(line[cursor..typeEnd], cursor, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitGoMultiNameValueDeclarationTypes(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var cursor = SkipWhitespace(line, 0);
        if (StartsWithKeyword(line, cursor, "var"))
            cursor = SkipWhitespace(line, cursor + "var".Length);
        else if (StartsWithKeyword(line, cursor, "const"))
            cursor = SkipWhitespace(line, cursor + "const".Length);

        if (!TryReadGoIdentifierList(line, ref cursor, requireComma: true))
            return;

        var typeStart = SkipWhitespace(line, cursor);
        if (typeStart >= line.Length || line[typeStart] == '=' || line.AsSpan(typeStart).StartsWith(":=", StringComparison.Ordinal))
            return;

        var typeEnd = typeStart;
        while (typeEnd < line.Length && line[typeEnd] != '=' && line[typeEnd] != '{')
            typeEnd++;

        var expression = line[typeStart..typeEnd].TrimEnd();
        EmitGoTypeExpression(expression, typeStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitGoSingleNameValueDeclarationTypes(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var cursor = SkipWhitespace(line, 0);
        if (StartsWithKeyword(line, cursor, "var"))
            cursor = SkipWhitespace(line, cursor + "var".Length);
        else if (StartsWithKeyword(line, cursor, "const"))
            cursor = SkipWhitespace(line, cursor + "const".Length);
        else
            return;

        if (cursor >= line.Length || !IsIdentifierStart(line[cursor]))
            return;

        cursor++;
        while (cursor < line.Length && IsSimpleIdentifierPart(line[cursor]))
            cursor++;

        var afterName = SkipWhitespace(line, cursor);
        if (afterName < line.Length && line[afterName] == ',')
            return;

        var typeStart = afterName;
        if (typeStart >= line.Length || line[typeStart] == '=' || line.AsSpan(typeStart).StartsWith(":=", StringComparison.Ordinal))
            return;
        if (!IsGoTypeExpressionStart(line, typeStart))
            return;

        var typeEnd = FindGoInlineTypeExpressionEnd(line, typeStart);
        if (typeEnd <= typeStart)
            return;

        var expression = line[typeStart..typeEnd].TrimEnd();
        EmitGoTypeExpression(expression, typeStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitGoMultiNameFieldDeclarationTypes(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var cursor = SkipWhitespace(line, 0);
        if (cursor >= line.Length || !IsIdentifierStart(line[cursor]))
            return;

        var nameStart = cursor;
        cursor++;
        while (cursor < line.Length && IsSimpleIdentifierPart(line[cursor]))
            cursor++;

        if (IsGoStatementKeyword(line[nameStart..cursor]))
            return;

        cursor = nameStart;
        if (!TryReadGoIdentifierList(line, ref cursor, requireComma: true))
            return;

        var typeStart = SkipWhitespace(line, cursor);
        if (typeStart >= line.Length || line[typeStart] is ':' or '=' || !IsGoTypeExpressionStart(line, typeStart))
            return;

        var typeEnd = FindGoInlineTypeExpressionEnd(line, typeStart);
        if (typeEnd <= typeStart)
            return;

        var afterType = SkipWhitespace(line, typeEnd);
        if (afterType < line.Length && line[afterType] != '`')
            return;

        EmitGoTypeExpression(line[typeStart..typeEnd], typeStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitGoSingleNameFieldDeclarationTypes(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var cursor = SkipWhitespace(line, 0);
        if (cursor >= line.Length || !IsIdentifierStart(line[cursor]))
            return;

        var nameStart = cursor;
        cursor++;
        while (cursor < line.Length && IsSimpleIdentifierPart(line[cursor]))
            cursor++;

        if (IsGoStatementKeyword(line[nameStart..cursor]))
            return;

        var typeStart = SkipWhitespace(line, cursor);
        if (typeStart >= line.Length || line[typeStart] is ':' or '=' or '(' || !IsGoTypeExpressionStart(line, typeStart))
            return;
        if (!IsLikelyGoFieldDeclarationTypeStart(line, typeStart))
            return;

        var typeEnd = FindGoInlineTypeExpressionEnd(line, typeStart);
        if (typeEnd <= typeStart)
            return;

        var afterType = SkipWhitespace(line, typeEnd);
        if (afterType < line.Length && line[afterType] != '`')
            return;

        EmitGoTypeExpression(line[typeStart..typeEnd], typeStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static bool IsLikelyGoFieldDeclarationTypeStart(string line, int typeStart)
    {
        if (typeStart >= line.Length || line[typeStart] != '[')
            return true;

        var close = ReferenceExtractor.FindMatchingChar(line, typeStart, '[', ']');
        if (close < 0)
            return false;

        var elementStart = SkipWhitespace(line, close + 1);
        return elementStart < line.Length && IsGoTypeExpressionStart(line, elementStart);
    }

    private static void EmitGoInterfaceTypeSetTermReferences(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        if (!line.Contains('|'))
            return;

        var first = SkipWhitespace(line, 0);
        if (first >= line.Length || line.Contains(":=", StringComparison.Ordinal) || line.Contains('='))
            return;
        if (IsIdentifierStart(line[first]))
        {
            var nameEnd = first + 1;
            while (nameEnd < line.Length && IsSimpleIdentifierPart(line[nameEnd]))
                nameEnd++;
            if (IsGoStatementKeyword(line[first..nameEnd]))
                return;
        }

        foreach (var (termStart, termLength) in SplitGoTypeSetTermSpans(line))
        {
            var rawTerm = line.Substring(termStart, termLength);
            var term = rawTerm.Trim();
            if (term.Length == 0)
                continue;

            var tildeOffset = term[0] == '~' ? 1 : 0;
            while (tildeOffset < term.Length && char.IsWhiteSpace(term[tildeOffset]))
                tildeOffset++;
            if (tildeOffset >= term.Length || !IsGoTypeExpressionStart(term, tildeOffset))
                continue;

            var expression = term[tildeOffset..];
            if (!ContainsLikelyGoTypeArgument(expression))
                continue;

            var termTrimStart = rawTerm.IndexOf(term, StringComparison.Ordinal);
            EmitGoTypeExpression(expression, termStart + Math.Max(0, termTrimStart) + tildeOffset, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static List<(int Start, int Length)> SplitGoTypeSetTermSpans(string line)
    {
        var spans = new List<(int Start, int Length)>();
        var termStart = 0;
        var squareDepth = 0;
        var parenDepth = 0;
        for (var cursor = 0; cursor < line.Length; cursor++)
        {
            switch (line[cursor])
            {
                case '[':
                    squareDepth++;
                    break;
                case ']':
                    if (squareDepth > 0)
                        squareDepth--;
                    break;
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    if (parenDepth > 0)
                        parenDepth--;
                    break;
                case '|':
                    if (squareDepth == 0 && parenDepth == 0)
                    {
                        spans.Add((termStart, cursor - termStart));
                        termStart = cursor + 1;
                    }
                    break;
            }
        }

        spans.Add((termStart, line.Length - termStart));
        return spans;
    }

    private static void EmitGoStandaloneTypeSetTermReferences(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        if (line.Contains('|'))
            return;

        var cursor = SkipWhitespace(line, 0);
        if (cursor >= line.Length || line[cursor] != '~')
            return;

        var typeStart = SkipWhitespace(line, cursor + 1);
        if (typeStart >= line.Length || !IsGoTypeExpressionStart(line, typeStart))
            return;

        var expression = line[typeStart..].TrimEnd();
        if (expression.Length == 0)
            return;

        EmitGoTypeExpression(expression, typeStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static bool TryReadGoIdentifierList(string line, ref int cursor, bool requireComma)
    {
        var count = 0;
        while (cursor < line.Length)
        {
            cursor = SkipWhitespace(line, cursor);
            if (cursor >= line.Length || !IsIdentifierStart(line[cursor]))
                break;

            cursor++;
            while (cursor < line.Length && IsSimpleIdentifierPart(line[cursor]))
                cursor++;

            count++;
            var afterName = SkipWhitespace(line, cursor);
            if (afterName >= line.Length || line[afterName] != ',')
            {
                cursor = afterName;
                break;
            }

            cursor = afterName + 1;
        }

        return requireComma ? count > 1 : count > 0;
    }

    private static void EmitGoEmbeddedFieldType(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var cursor = SkipWhitespace(line, 0);
        if (cursor >= line.Length)
            return;

        if (line[cursor] == '*')
            cursor = SkipWhitespace(line, cursor + 1);

        if (cursor >= line.Length || !IsIdentifierStart(line[cursor]))
            return;

        var nameStart = cursor;
        cursor++;
        while (cursor < line.Length && IsSimpleIdentifierPart(line[cursor]))
            cursor++;

        if (IsGoStatementKeyword(line[nameStart..cursor]))
            return;

        if (cursor < line.Length && line[cursor] == '.')
        {
            cursor++;
            if (cursor >= line.Length || !IsIdentifierStart(line[cursor]))
                return;
            cursor++;
            while (cursor < line.Length && IsSimpleIdentifierPart(line[cursor]))
                cursor++;
        }

        if (cursor < line.Length && line[cursor] == '[')
        {
            var close = ReferenceExtractor.FindMatchingChar(line, cursor, '[', ']');
            if (close < 0)
                return;
            cursor = close + 1;
        }

        var afterType = SkipWhitespace(line, cursor);
        if (afterType < line.Length && line[afterType] != '`')
            return;

        var typeStart = line.IndexOf('*') >= 0 && line.IndexOf('*') < nameStart
            ? line.IndexOf('*')
            : nameStart;
        EmitGoTypeExpression(line[typeStart..cursor], typeStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitGoBuiltinTypeArgumentReferences(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (Match match in GoBuiltinTypeArgumentRegex.Matches(line))
        {
            var open = line.IndexOf('(', match.Index);
            if (open < 0)
                continue;

            var close = ReferenceExtractor.FindMatchingChar(line, open, '(', ')');
            if (close < 0)
                continue;

            var argumentList = line[(open + 1)..close];
            var firstArgument = ReferenceExtractor.SplitTopLevelCommaSpans(argumentList).FirstOrDefault();
            if (firstArgument.Length <= 0)
                continue;

            var rawType = argumentList.Substring(firstArgument.Start, firstArgument.Length);
            var expression = rawType.Trim();
            if (expression.Length == 0)
                continue;

            var trimStart = rawType.IndexOf(expression, StringComparison.Ordinal);
            var absoluteStart = open + 1 + firstArgument.Start + Math.Max(0, trimStart);
            EmitGoTypeExpression(expression, absoluteStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static void EmitGoTypeAssertionReferences(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (Match match in GoTypeAssertionRegex.Matches(line))
        {
            var group = match.Groups["type"];
            var expression = group.Value.Trim();
            if (expression.Length == 0 || string.Equals(expression, "type", StringComparison.Ordinal))
                continue;

            var trimStart = group.Value.IndexOf(expression, StringComparison.Ordinal);
            EmitGoTypeExpression(expression, group.Index + Math.Max(0, trimStart), references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static void EmitGoTypeSwitchCaseReferences(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var match = GoTypeSwitchCaseRegex.Match(line);
        if (!match.Success)
            return;

        var group = match.Groups["types"];
        foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(group.Value))
        {
            var rawType = group.Value.Substring(segmentStart, segmentLength);
            var expression = rawType.Trim();
            if (expression.Length == 0
                || expression is "nil" or "default"
                || !IsLikelyGoTypeSwitchCaseType(expression))
            {
                continue;
            }

            var trimStart = rawType.IndexOf(expression, StringComparison.Ordinal);
            EmitGoTypeExpression(expression, group.Index + segmentStart + Math.Max(0, trimStart), references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static bool IsLikelyGoTypeSwitchCaseType(string expression)
    {
        var cursor = 0;
        var hasPointerPrefix = false;
        while (cursor < expression.Length)
        {
            cursor = SkipWhitespace(expression, cursor);
            if (cursor >= expression.Length || expression[cursor] != '*')
                break;

            hasPointerPrefix = true;
            cursor++;
        }

        if (cursor >= expression.Length)
            return false;
        if (expression[cursor] == '[')
            return true;
        if (hasPointerPrefix && char.IsUpper(expression[cursor]))
            return true;
        if (hasPointerPrefix && expression.IndexOf('.', cursor) >= 0)
            return true;

        return StartsWithKeyword(expression, cursor, "map")
            || StartsWithKeyword(expression, cursor, "chan")
            || StartsWithKeyword(expression, cursor, "func")
            || StartsWithKeyword(expression, cursor, "interface")
            || StartsWithKeyword(expression, cursor, "struct");
    }

    private static void EmitGoChannelElementTypeReferences(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var searchStart = 0;
        while (searchStart < line.Length)
        {
            var chanIndex = line.IndexOf("chan", searchStart, StringComparison.Ordinal);
            if (chanIndex < 0)
                return;

            searchStart = chanIndex + "chan".Length;
            if (!IsIdentifierAt(line, chanIndex, "chan"))
                continue;

            var elementStart = SkipWhitespace(line, searchStart);
            if (elementStart + 1 < line.Length && line[elementStart] == '<' && line[elementStart + 1] == '-')
                elementStart = SkipWhitespace(line, elementStart + 2);

            if (elementStart >= line.Length || !IsGoTypeExpressionStart(line, elementStart))
                continue;

            var elementEnd = FindGoInlineTypeExpressionEnd(line, elementStart);
            if (elementEnd <= elementStart)
                continue;

            EmitGoTypeExpression(line[elementStart..elementEnd], elementStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static void EmitGoFunctionLiteralSignatureTypes(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (Match match in GoFunctionLiteralRegex.Matches(line))
        {
            var open = line.IndexOf('(', match.Index);
            if (open < 0)
                continue;

            var close = ReferenceExtractor.FindMatchingChar(line, open, '(', ')');
            if (close < 0)
                continue;

            EmitGoParameterListTypes(line, open + 1, close, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
            EmitGoSignatureReturnTypes(line, close + 1, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static void EmitGoGenericCallTypeArgumentReferences(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        if (GoFuncRegex.IsMatch(line))
            return;

        var searchStart = 0;
        while (searchStart < line.Length)
        {
            var open = line.IndexOf('[', searchStart);
            if (open < 0)
                return;

            searchStart = open + 1;
            if (!HasGoIdentifierBeforeBracket(line, open))
                continue;

            var close = ReferenceExtractor.FindMatchingChar(line, open, '[', ']');
            if (close < 0)
                continue;

            var afterClose = SkipWhitespace(line, close + 1);
            if (afterClose >= line.Length || line[afterClose] != '(')
                continue;

            var typeArguments = line[(open + 1)..close];
            foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(typeArguments))
            {
                var rawArgument = typeArguments.Substring(segmentStart, segmentLength);
                var expression = rawArgument.Trim();
                if (expression.Length == 0 || !ContainsLikelyGoTypeArgument(expression))
                    continue;

                var trimStart = rawArgument.IndexOf(expression, StringComparison.Ordinal);
                var absoluteStart = open + 1 + segmentStart + Math.Max(0, trimStart);
                EmitGoTypeExpression(expression, absoluteStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
            }
        }
    }

    private static void EmitGoFunctionTypeSignatureTypes(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var searchStart = 0;
        while (searchStart < line.Length)
        {
            var funcIndex = line.IndexOf("func", searchStart, StringComparison.Ordinal);
            if (funcIndex < 0)
                return;

            searchStart = funcIndex + "func".Length;
            if (!IsIdentifierAt(line, funcIndex, "func"))
                continue;

            var open = SkipWhitespace(line, searchStart);
            if (open >= line.Length || line[open] != '(')
                continue;

            var close = ReferenceExtractor.FindMatchingChar(line, open, '(', ')');
            if (close < 0)
                continue;

            EmitGoParameterListTypes(line, open + 1, close, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
            EmitGoSignatureReturnTypes(line, close + 1, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static void EmitGoGenericCompositeLiteralReferences(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var searchStart = 0;
        while (searchStart < line.Length)
        {
            var open = line.IndexOf('[', searchStart);
            if (open < 0)
                return;

            searchStart = open + 1;
            if (!TryGetGoIdentifierBeforeBracket(line, open, out var nameStart, out var nameLength))
                continue;

            var typeName = line.Substring(nameStart, nameLength);
            if (typeName.Length == 0 || !char.IsUpper(typeName[0]))
                continue;

            var close = ReferenceExtractor.FindMatchingChar(line, open, '[', ']');
            if (close < 0)
                continue;

            var afterClose = SkipWhitespace(line, close + 1);
            if (afterClose >= line.Length || line[afterClose] != '{')
                continue;

            if (!IsGoCompositeLiteralContext(line, nameStart, nameLength))
                continue;

            ReferenceExtractor.AddReference(references, seen, fileId, typeName, nameStart, "instantiate", context, lineNumber, resolveContainerForColumn(nameStart));

            var typeArguments = line[(open + 1)..close];
            foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(typeArguments))
            {
                var rawArgument = typeArguments.Substring(segmentStart, segmentLength);
                var expression = rawArgument.Trim();
                if (expression.Length == 0 || !ContainsLikelyGoTypeArgument(expression))
                    continue;

                var trimStart = rawArgument.IndexOf(expression, StringComparison.Ordinal);
                var absoluteStart = open + 1 + segmentStart + Math.Max(0, trimStart);
                EmitGoTypeExpression(expression, absoluteStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
            }
        }
    }

    private static void EmitGoInlineStructFieldTypeReferences(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var searchStart = 0;
        while (searchStart < line.Length)
        {
            var structIndex = line.IndexOf("struct", searchStart, StringComparison.Ordinal);
            if (structIndex < 0)
                return;

            searchStart = structIndex + "struct".Length;
            if (!IsIdentifierAt(line, structIndex, "struct"))
                continue;

            var open = SkipWhitespace(line, searchStart);
            if (open >= line.Length || line[open] != '{')
                continue;

            var close = ReferenceExtractor.FindMatchingChar(line, open, '{', '}');
            if (close <= open + 1)
                continue;

            var body = line[(open + 1)..close];
            var bodyStart = open + 1;
            foreach (var (fieldStart, fieldLength) in SplitGoInlineStructFieldSpans(body))
                EmitGoInlineStructFieldType(body.Substring(fieldStart, fieldLength), bodyStart + fieldStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static List<(int Start, int Length)> SplitGoInlineStructFieldSpans(string body)
    {
        var spans = new List<(int Start, int Length)>();
        var fieldStart = 0;
        var squareDepth = 0;
        var parenDepth = 0;
        for (var cursor = 0; cursor < body.Length; cursor++)
        {
            switch (body[cursor])
            {
                case '[':
                    squareDepth++;
                    break;
                case ']':
                    if (squareDepth > 0)
                        squareDepth--;
                    break;
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    if (parenDepth > 0)
                        parenDepth--;
                    break;
                case ';':
                    if (squareDepth == 0 && parenDepth == 0)
                    {
                        spans.Add((fieldStart, cursor - fieldStart));
                        fieldStart = cursor + 1;
                    }
                    break;
            }
        }

        spans.Add((fieldStart, body.Length - fieldStart));
        return spans;
    }

    private static void EmitGoInlineStructFieldType(
        string rawField,
        int rawFieldStart,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var tagStart = rawField.IndexOf('`');
        if (tagStart >= 0)
            rawField = rawField[..tagStart];

        var field = rawField.Trim();
        if (field.Length == 0)
            return;

        var fieldTrimStart = rawField.IndexOf(field, StringComparison.Ordinal);
        var absoluteFieldStart = rawFieldStart + Math.Max(0, fieldTrimStart);
        var typeStart = LastWhitespaceSeparatedTokenStart(field);
        if (typeStart < 0)
            return;

        var expression = typeStart == 0 ? field : field[typeStart..];
        EmitGoTypeExpression(expression, absoluteFieldStart + typeStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitGoInlineInterfaceMemberTypeReferences(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var searchStart = 0;
        while (searchStart < line.Length)
        {
            var interfaceIndex = line.IndexOf("interface", searchStart, StringComparison.Ordinal);
            if (interfaceIndex < 0)
                return;

            searchStart = interfaceIndex + "interface".Length;
            if (!IsIdentifierAt(line, interfaceIndex, "interface"))
                continue;

            var open = SkipWhitespace(line, searchStart);
            if (open >= line.Length || line[open] != '{')
                continue;

            var close = ReferenceExtractor.FindMatchingChar(line, open, '{', '}');
            if (close <= open + 1)
                continue;

            var body = line[(open + 1)..close];
            var bodyStart = open + 1;
            foreach (var (memberStart, memberLength) in SplitGoInlineStructFieldSpans(body))
                EmitGoInlineInterfaceMemberTypes(line, bodyStart + memberStart, bodyStart + memberStart + memberLength, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static void EmitGoInlineInterfaceMemberTypes(
        string line,
        int memberStart,
        int memberEnd,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var cursor = SkipWhitespace(line, memberStart);
        if (cursor >= memberEnd)
            return;

        if (!IsIdentifierStart(line[cursor]))
        {
            EmitGoInlineInterfaceEmbeddedType(line, cursor, memberEnd, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
            return;
        }

        var nameStart = cursor;
        cursor++;
        while (cursor < memberEnd && IsSimpleIdentifierPart(line[cursor]))
            cursor++;

        var name = line[nameStart..cursor];
        if (IsGoStatementKeyword(name))
            return;

        var open = SkipWhitespace(line, cursor);
        if (open < memberEnd && line[open] == '(')
        {
            var close = ReferenceExtractor.FindMatchingChar(line, open, '(', ')');
            if (close > open && close <= memberEnd)
            {
                EmitGoParameterListTypes(line, open + 1, close, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
                EmitGoSignatureReturnTypesInRange(line, close + 1, memberEnd, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
            }

            return;
        }

        EmitGoInlineInterfaceEmbeddedType(line, nameStart, memberEnd, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitGoInlineInterfaceEmbeddedType(
        string line,
        int start,
        int end,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var typeStart = SkipWhitespace(line, start);
        if (typeStart >= end)
            return;

        var expression = line[typeStart..end].Trim();
        if (expression.Length == 0)
            return;

        EmitGoTypeExpression(expression, typeStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitGoSignatureReturnTypesInRange(
        string line,
        int start,
        int end,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var returnStart = SkipWhitespace(line, start);
        if (returnStart >= end || line[returnStart] == '{')
            return;

        if (line[returnStart] == '(')
        {
            var returnClose = ReferenceExtractor.FindMatchingChar(line, returnStart, '(', ')');
            if (returnClose > returnStart && returnClose <= end)
                EmitGoParameterListTypes(line, returnStart + 1, returnClose, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
            return;
        }

        var expression = line[returnStart..end].TrimEnd();
        EmitGoTypeExpression(expression, returnStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitGoMapCompositeLiteralTypeReferences(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var searchStart = 0;
        while (searchStart < line.Length)
        {
            var mapIndex = line.IndexOf("map", searchStart, StringComparison.Ordinal);
            if (mapIndex < 0)
                return;

            searchStart = mapIndex + "map".Length;
            if (!IsIdentifierAt(line, mapIndex, "map"))
                continue;

            var open = SkipWhitespace(line, searchStart);
            if (open >= line.Length || line[open] != '[')
                continue;

            var close = ReferenceExtractor.FindMatchingChar(line, open, '[', ']');
            if (close < 0)
                continue;

            var valueStart = SkipWhitespace(line, close + 1);
            if (valueStart >= line.Length || !IsGoTypeExpressionStart(line, valueStart))
                continue;

            var valueEnd = FindGoInlineTypeExpressionEnd(line, valueStart);
            var literalOpen = SkipWhitespace(line, valueEnd);
            if (literalOpen >= line.Length || line[literalOpen] != '{')
                continue;

            var keyExpression = line[(open + 1)..close].Trim();
            if (keyExpression.Length > 0)
            {
                var keyStart = line.IndexOf(keyExpression, open + 1, StringComparison.Ordinal);
                EmitGoTypeExpression(keyExpression, keyStart >= 0 ? keyStart : open + 1, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
            }

            EmitGoTypeExpression(line[valueStart..valueEnd], valueStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static void EmitGoArraySliceCompositeLiteralTypeReferences(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var searchStart = 0;
        while (searchStart < line.Length)
        {
            var open = line.IndexOf('[', searchStart);
            if (open < 0)
                return;

            searchStart = open + 1;
            var close = ReferenceExtractor.FindMatchingChar(line, open, '[', ']');
            if (close < 0)
                continue;

            var elementStart = SkipWhitespace(line, close + 1);
            if (elementStart >= line.Length || !IsGoTypeExpressionStart(line, elementStart))
                continue;

            var elementEnd = FindGoInlineTypeExpressionEnd(line, elementStart);
            var literalOpen = SkipWhitespace(line, elementEnd);
            if (literalOpen >= line.Length || line[literalOpen] != '{')
                continue;

            if (!IsGoCompositeLiteralContext(line, open, elementEnd - open))
                continue;

            EmitGoTypeExpression(line[elementStart..elementEnd], elementStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static void EmitGoParenthesizedTypeConversionReferences(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var searchStart = 0;
        while (searchStart < line.Length)
        {
            var open = line.IndexOf('(', searchStart);
            if (open < 0)
                return;

            searchStart = open + 1;
            var close = ReferenceExtractor.FindMatchingChar(line, open, '(', ')');
            if (close <= open + 1)
                continue;

            var afterClose = SkipWhitespace(line, close + 1);
            if (afterClose >= line.Length || line[afterClose] != '(')
                continue;

            var rawExpression = line[(open + 1)..close];
            var expression = rawExpression.Trim();
            if (!IsLikelyGoParenthesizedConversionType(expression))
                continue;

            var trimStart = rawExpression.IndexOf(expression, StringComparison.Ordinal);
            EmitGoTypeExpression(expression, open + 1 + Math.Max(0, trimStart), references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static void EmitGoCompositeTypeConversionReferences(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var searchStart = 0;
        while (searchStart < line.Length)
        {
            var typeStart = NextGoCompositeConversionTypeStart(line, searchStart);
            if (typeStart < 0)
                return;

            searchStart = typeStart + 1;
            if (!IsGoTypeExpressionValueContext(line, typeStart))
                continue;

            var typeEnd = FindGoConversionTypeExpressionEnd(line, typeStart);
            if (typeEnd <= typeStart)
                continue;

            var open = SkipWhitespace(line, typeEnd);
            if (open >= line.Length || line[open] != '(')
                continue;
            if (ReferenceExtractor.FindMatchingChar(line, open, '(', ')') < 0)
                continue;

            EmitGoTypeExpression(line[typeStart..typeEnd], typeStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static int NextGoCompositeConversionTypeStart(string line, int searchStart)
    {
        for (var cursor = searchStart; cursor < line.Length; cursor++)
        {
            if (line[cursor] == '[')
                return cursor;
            if (IsIdentifierAt(line, cursor, "map") || IsIdentifierAt(line, cursor, "chan"))
                return cursor;
        }

        return -1;
    }

    private static int FindGoConversionTypeExpressionEnd(string line, int start)
    {
        var squareDepth = 0;
        for (var cursor = start; cursor < line.Length; cursor++)
        {
            switch (line[cursor])
            {
                case '[':
                    squareDepth++;
                    break;
                case ']':
                    if (squareDepth > 0)
                        squareDepth--;
                    break;
                case '(':
                case ',':
                case '{':
                case '`':
                case '=':
                case ';':
                    if (squareDepth == 0)
                        return cursor;
                    break;
            }
        }

        return line.Length;
    }

    private static bool IsGoTypeExpressionValueContext(string line, int start)
    {
        var previous = start - 1;
        while (previous >= 0 && char.IsWhiteSpace(line[previous]))
            previous--;
        if (previous < 0)
            return true;
        if (line[previous] is '=' or ':' or '(' or '[' or '{' or ',' or '!' or '&' or '*')
            return true;

        var tokenEnd = previous + 1;
        while (previous >= 0 && IsSimpleIdentifierPart(line[previous]))
            previous--;
        var token = line[(previous + 1)..tokenEnd];
        return string.Equals(token, "return", StringComparison.Ordinal);
    }

    private static bool IsLikelyGoParenthesizedConversionType(string expression)
    {
        if (expression.Length == 0 || expression.Contains(','))
            return false;

        var cursor = 0;
        while (cursor < expression.Length && char.IsWhiteSpace(expression[cursor]))
            cursor++;
        var isPointerConversion = cursor < expression.Length && expression[cursor] == '*';
        while (cursor < expression.Length && expression[cursor] == '*')
            cursor = SkipWhitespace(expression, cursor + 1);

        if (cursor >= expression.Length || !IsIdentifierStart(expression[cursor]))
        {
            return cursor < expression.Length && expression[cursor] == '[';
        }

        if (StartsWithKeyword(expression, cursor, "map")
            || StartsWithKeyword(expression, cursor, "chan"))
        {
            return true;
        }

        var lastSegmentStart = cursor;
        while (cursor < expression.Length)
        {
            if (IsSimpleIdentifierPart(expression[cursor]))
            {
                cursor++;
                continue;
            }

            if (expression[cursor] != '.')
                return false;

            cursor++;
            if (cursor >= expression.Length || !IsIdentifierStart(expression[cursor]))
                return false;
            lastSegmentStart = cursor;
            cursor++;
        }

        return isPointerConversion || expression.Contains('.');
    }

    private static void EmitGoMethodExpressionReceiverTypeReferences(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        EmitGoParenthesizedMethodExpressionReceiverTypes(line, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoBareMethodExpressionReceiverTypes(line, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoGenericMethodExpressionReceiverTypes(line, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitGoParenthesizedMethodExpressionReceiverTypes(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var searchStart = 0;
        while (searchStart < line.Length)
        {
            var open = line.IndexOf('(', searchStart);
            if (open < 0)
                return;

            searchStart = open + 1;
            var close = ReferenceExtractor.FindMatchingChar(line, open, '(', ')');
            if (close <= open + 1)
                continue;

            var dot = SkipWhitespace(line, close + 1);
            if (dot >= line.Length || line[dot] != '.')
                continue;

            var methodStart = dot + 1;
            if (methodStart >= line.Length || !IsIdentifierStart(line[methodStart]))
                continue;

            var rawExpression = line[(open + 1)..close];
            var expression = rawExpression.Trim();
            if (!IsLikelyGoMethodExpressionReceiverType(expression))
                continue;

            var trimStart = rawExpression.IndexOf(expression, StringComparison.Ordinal);
            EmitGoTypeExpression(expression, open + 1 + Math.Max(0, trimStart), references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static void EmitGoBareMethodExpressionReceiverTypes(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        for (var dot = 1; dot < line.Length - 1; dot++)
        {
            if (line[dot] != '.')
                continue;
            if (!IsSimpleIdentifierPart(line[dot - 1]) || !IsIdentifierStart(line[dot + 1]))
                continue;

            var receiverStart = dot - 1;
            while (receiverStart >= 0 && IsSimpleIdentifierPart(line[receiverStart]))
                receiverStart--;
            receiverStart++;

            var receiverName = line[receiverStart..dot];
            if (receiverName.Length == 0 || !char.IsUpper(receiverName[0]))
                continue;

            ReferenceExtractor.AddReference(references, seen, fileId, receiverName, receiverStart, "type_reference", context, lineNumber, resolveContainerForColumn(receiverStart));
        }
    }

    private static bool IsLikelyGoMethodExpressionReceiverType(string expression)
    {
        if (expression.Length == 0 || expression.Contains(','))
            return false;

        var cursor = 0;
        while (cursor < expression.Length && char.IsWhiteSpace(expression[cursor]))
            cursor++;
        if (cursor < expression.Length && expression[cursor] == '*')
            cursor = SkipWhitespace(expression, cursor + 1);

        if (cursor >= expression.Length || !IsIdentifierStart(expression[cursor]))
            return false;

        if (IsLikelyGoGenericReceiverTypeExpression(expression, cursor))
            return true;

        var lastSegmentStart = cursor;
        while (cursor < expression.Length)
        {
            if (IsSimpleIdentifierPart(expression[cursor]))
            {
                cursor++;
                continue;
            }

            if (expression[cursor] != '.')
                return false;

            cursor++;
            if (cursor >= expression.Length || !IsIdentifierStart(expression[cursor]))
                return false;
            lastSegmentStart = cursor;
            cursor++;
        }

        return expression.Contains('.') || char.IsUpper(expression[lastSegmentStart]);
    }

    private static void EmitGoGenericMethodExpressionReceiverTypes(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var searchStart = 0;
        while (searchStart < line.Length)
        {
            var open = line.IndexOf('[', searchStart);
            if (open < 0)
                return;

            searchStart = open + 1;
            if (!TryGetGoIdentifierBeforeBracket(line, open, out var nameStart, out var nameLength))
                continue;
            if (nameLength == 0 || !char.IsUpper(line[nameStart]))
                continue;

            var close = ReferenceExtractor.FindMatchingChar(line, open, '[', ']');
            if (close < 0)
                continue;

            var dot = SkipWhitespace(line, close + 1);
            if (dot >= line.Length || line[dot] != '.')
                continue;

            var methodStart = dot + 1;
            if (methodStart >= line.Length || !IsIdentifierStart(line[methodStart]))
                continue;

            EmitGoTypeExpression(line[nameStart..(close + 1)], nameStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static bool IsLikelyGoGenericReceiverTypeExpression(string expression, int receiverStart)
    {
        var open = expression.IndexOf('[', receiverStart);
        if (open < 0 || !ContainsLikelyGoTypeArgument(expression[open..]))
            return false;

        var firstSegmentStart = receiverStart;
        var firstSegmentEnd = firstSegmentStart;
        while (firstSegmentEnd < expression.Length && IsSimpleIdentifierPart(expression[firstSegmentEnd]))
            firstSegmentEnd++;

        if (firstSegmentEnd <= firstSegmentStart)
            return false;
        if (char.IsUpper(expression[firstSegmentStart]))
            return true;

        var afterFirst = SkipWhitespace(expression, firstSegmentEnd);
        return afterFirst < expression.Length && expression[afterFirst] == '.';
    }

    private static void EmitGoGenericInstantiationTypeArgumentReferences(
        string line,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        if (GoFuncRegex.IsMatch(line))
            return;

        var searchStart = 0;
        while (searchStart < line.Length)
        {
            var open = line.IndexOf('[', searchStart);
            if (open < 0)
                return;

            searchStart = open + 1;
            if (!TryGetGoIdentifierBeforeBracket(line, open, out var nameStart, out var nameLength))
                continue;
            if (nameLength == 0 || !char.IsUpper(line[nameStart]))
                continue;

            var close = ReferenceExtractor.FindMatchingChar(line, open, '[', ']');
            if (close < 0)
                continue;

            var afterClose = SkipWhitespace(line, close + 1);
            if (afterClose < line.Length && line[afterClose] is '(' or '{')
                continue;
            if (afterClose < line.Length && !IsGoGenericInstantiationTerminator(line[afterClose]))
                continue;

            EmitGoGenericTypeArgumentList(line, open, close, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static bool IsGoGenericInstantiationTerminator(char ch)
        => char.IsWhiteSpace(ch) || ch is ',' or ')' or ']' or '}' or ';';

    private static void EmitGoGenericTypeArgumentList(
        string line,
        int open,
        int close,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var typeArguments = line[(open + 1)..close];
        foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(typeArguments))
        {
            var rawArgument = typeArguments.Substring(segmentStart, segmentLength);
            var expression = rawArgument.Trim();
            if (expression.Length == 0 || !ContainsLikelyGoTypeArgument(expression))
                continue;

            var trimStart = rawArgument.IndexOf(expression, StringComparison.Ordinal);
            var absoluteStart = open + 1 + segmentStart + Math.Max(0, trimStart);
            EmitGoTypeExpression(expression, absoluteStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static bool HasGoIdentifierBeforeBracket(string line, int openBracket)
        => TryGetGoIdentifierBeforeBracket(line, openBracket, out _, out _);

    private static bool TryGetGoIdentifierBeforeBracket(string line, int openBracket, out int start, out int length)
    {
        start = -1;
        length = 0;
        var cursor = openBracket - 1;
        while (cursor >= 0 && char.IsWhiteSpace(line[cursor]))
            cursor--;
        if (cursor < 0 || !IsSimpleIdentifierPart(line[cursor]))
            return false;

        var end = cursor + 1;
        while (cursor >= 0 && IsSimpleIdentifierPart(line[cursor]))
            cursor--;

        start = cursor + 1;
        length = end - start;
        return true;
    }

    private static bool ContainsLikelyGoTypeArgument(string expression)
    {
        for (var i = 0; i < expression.Length; i++)
        {
            if (!IsIdentifierStart(expression[i]))
                continue;

            var start = i;
            i++;
            while (i < expression.Length && IsSimpleIdentifierPart(expression[i]))
                i++;

            if (char.IsUpper(expression[start]))
                return true;
        }

        return false;
    }

    private static bool StartsWithKeyword(string line, int index, string keyword)
    {
        if (index < 0 || index + keyword.Length > line.Length)
            return false;
        if (!line.AsSpan(index, keyword.Length).SequenceEqual(keyword))
            return false;

        var beforeOk = index == 0 || !IsSimpleIdentifierPart(line[index - 1]);
        var after = index + keyword.Length;
        var afterOk = after >= line.Length || !IsSimpleIdentifierPart(line[after]);
        return beforeOk && afterOk;
    }

    private static bool IsGoTypeDeclarationBodyStart(string line, int index)
    {
        if (StartsWithKeyword(line, index, "struct")
            || StartsWithKeyword(line, index, "interface")
            || StartsWithKeyword(line, index, "func")
            || StartsWithKeyword(line, index, "map")
            || StartsWithKeyword(line, index, "chan"))
        {
            return true;
        }

        return line[index] is '*' or '[' or '~' || IsIdentifierStart(line[index]);
    }

    private static bool IsGoCompositeLiteralContext(string line, int nameIndex, int nameLength)
    {
        var openBraceIndex = line.IndexOf('{', nameIndex + nameLength);
        if (openBraceIndex < 0)
            return false;

        var trimmed = line.TrimStart();
        var firstBraceIndex = line.IndexOf('{');
        if (trimmed.StartsWith("func ", StringComparison.Ordinal) && firstBraceIndex == openBraceIndex)
            return false;

        var previous = nameIndex - 1;
        while (previous >= 0 && char.IsWhiteSpace(line[previous]))
            previous--;
        if (previous < 0)
            return false;

        if (line[previous] is '=' or ':' or '(' or '[' or '{' or ',' or '!' or '&' or '*')
            return true;
        if (line[previous] == '.')
            return previous > 0 && IsSimpleIdentifierPart(line[previous - 1]);
        if (line[previous] == ']')
            return !trimmed.StartsWith("func ", StringComparison.Ordinal);

        var tokenEnd = previous + 1;
        while (previous >= 0 && IsSimpleIdentifierPart(line[previous]))
            previous--;
        var token = line[(previous + 1)..tokenEnd];
        return string.Equals(token, "return", StringComparison.Ordinal);
    }

    private static void EmitDartTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        TypedLanguageReferenceExtractor.EmitKeywordFollowingTypeReferences(
            preparedLine,
            ["extends", "with", "implements", "on", "as", "is"],
            "dart",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);
        TypedLanguageReferenceExtractor.EmitColonParameterTypeReferences(preparedLine, 0, preparedLine.Length, "dart", references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        TypedLanguageReferenceExtractor.EmitColonVariableTypeReferences(preparedLine, ["final", "var", "late", "const"], "dart", references, seen, fileId, context, lineNumber, resolveContainerForColumn);

        foreach (Match match in DartVariableTypeRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), "dart");
        }

        var signatureMatch = DartFunctionSignatureRegex.Match(preparedLine);
        if (signatureMatch.Success)
        {
            var returnGroup = signatureMatch.Groups["return"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, returnGroup.Value, returnGroup.Index, context, lineNumber, resolveContainerForColumn(returnGroup.Index), "dart");

            var parametersGroup = signatureMatch.Groups["params"];
            foreach (Match parameterMatch in DartParameterTypeRegex.Matches(parametersGroup.Value))
            {
                var typeGroup = parameterMatch.Groups["type"];
                var absoluteIndex = parametersGroup.Index + typeGroup.Index;
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, typeGroup.Value, absoluteIndex, context, lineNumber, resolveContainerForColumn(absoluteIndex), "dart");
            }
        }

        foreach (Match match in DartCtorRegex.Matches(preparedLine))
        {
            var group = match.Groups["name"];
            ReferenceExtractor.AddReference(references, seen, fileId, group.Value, group.Index, "instantiate", context, lineNumber, resolveContainerForColumn(group.Index));
        }
    }

    private static void EmitVbTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (Match match in VbTypeKeywordRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), "vb");
        }

        foreach (Match match in VbAddressOfRegex.Matches(preparedLine))
        {
            var group = match.Groups["name"];
            var name = LastQualifiedSegment(group.Value);
            var nameOffset = group.Value.LastIndexOf(name, StringComparison.Ordinal);
            var nameIndex = group.Index + Math.Max(0, nameOffset);
            ReferenceExtractor.AddReference(references, seen, fileId, name, nameIndex, "call", context, lineNumber, resolveContainerForColumn(nameIndex));
        }

        foreach (Match match in VbHandlesRegex.Matches(preparedLine))
            ReferenceExtractor.AddReference(references, seen, fileId, match, "subscribe", context, lineNumber, resolveContainerForColumn(match.Groups["name"].Index));
    }

    private static void EmitFortranTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        SymbolRecord? container)
    {
        foreach (Match match in FortranUseRegex.Matches(preparedLine))
            ReferenceExtractor.AddReference(references, seen, fileId, match, "type_reference", context, lineNumber, container);

        foreach (Match match in FortranTypeRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), "fortran");
        }
    }

    private static void EmitPascalTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        SymbolRecord? container)
    {
        var usesMatch = PascalUsesRegex.Match(preparedLine);
        if (usesMatch.Success)
            EmitCommaSeparatedNames(usesMatch.Groups["list"].Value, usesMatch.Groups["list"].Index, "pascal", references, seen, fileId, context, lineNumber, container);

        foreach (Match match in PascalClassBaseRegex.Matches(preparedLine))
            EmitCommaSeparatedNames(match.Groups["bases"].Value, match.Groups["bases"].Index, "pascal", references, seen, fileId, context, lineNumber, resolveContainerForColumn(match.Groups["bases"].Index));

        foreach (Match match in PascalTypeAfterColonRegex.Matches(preparedLine))
        {
            if (!IsPascalColonTypeReferenceContext(preparedLine, lineNumber, container))
                continue;

            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), "pascal");
        }
    }

    private static bool IsPascalColonTypeReferenceContext(string preparedLine, int lineNumber, SymbolRecord? container)
    {
        var trimmed = preparedLine.TrimStart();
        if (container?.Kind != "function"
            || !container.BodyStartLine.HasValue
            || lineNumber < container.BodyStartLine.Value)
        {
            return true;
        }

        return StartsWithPascalDeclarationKeyword(trimmed);
    }

    private static bool StartsWithPascalDeclarationKeyword(string trimmedLine)
    {
        foreach (var keyword in new[] { "var", "const", "type", "property", "procedure", "function", "constructor", "destructor" })
        {
            if (trimmedLine.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
                && (trimmedLine.Length == keyword.Length || !IsSimpleIdentifierPart(trimmedLine[keyword.Length])))
            {
                return true;
            }
        }

        return false;
    }

    private static void EmitObjCTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        SymbolRecord? container)
    {
        foreach (Match match in ObjCInterfaceBaseRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddReference(references, seen, fileId, group.Value, group.Index, "type_reference", context, lineNumber, container);
        }

        foreach (Match match in ObjCProtocolListRegex.Matches(preparedLine))
            EmitCommaSeparatedNames(match.Groups["list"].Value, match.Groups["list"].Index, "objc", references, seen, fileId, context, lineNumber, container);

        foreach (Match match in ObjCDeclTypeRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), "objc");
        }
    }

    private static void EmitHaskellTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        var match = HaskellSignatureRegex.Match(preparedLine);
        if (!match.Success)
            return;

        var group = match.Groups["types"];
        ReferenceExtractor.AddTypeExpressionSegments(
            references,
            seen,
            fileId,
            group.Value,
            group.Index,
            context,
            lineNumber,
            container,
            "haskell",
            BuildHaskellIgnoredTypeVariables(group.Value));
    }

    private static IReadOnlySet<string>? BuildHaskellIgnoredTypeVariables(string expression)
    {
        HashSet<string>? ignored = null;
        for (var cursor = 0; cursor < expression.Length; cursor++)
        {
            if (!IsSimpleIdentifierPart(expression[cursor]))
                continue;

            var start = cursor;
            while (cursor < expression.Length && IsSimpleIdentifierPart(expression[cursor]))
                cursor++;

            if (char.IsLower(expression[start]))
            {
                ignored ??= new HashSet<string>(StringComparer.Ordinal);
                ignored.Add(expression[start..cursor]);
            }

            cursor--;
        }

        return ignored;
    }

    private static void EmitElixirTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (var match in EnumerateMatches(ElixirImportRegex, preparedLine).Concat(EnumerateMatches(ElixirBehaviourRegex, preparedLine)))
            ReferenceExtractor.AddReference(references, seen, fileId, match, "type_reference", context, lineNumber, container);
    }

    private static void EmitLuaTypeReferences(
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (var (name, index) in EnumerateLuaRequireReferences(originalLine))
            ReferenceExtractor.AddReference(references, seen, fileId, name, index, "type_reference", context, lineNumber, container);
    }

    private static IEnumerable<(string Name, int Index)> EnumerateLuaRequireReferences(string line)
    {
        for (var cursor = 0; cursor < line.Length; cursor++)
        {
            if (line[cursor] == '-' && cursor + 1 < line.Length && line[cursor + 1] == '-')
                yield break;

            if (line[cursor] is '"' or '\'')
            {
                cursor = SkipQuotedLiteral(line, cursor);
                continue;
            }

            if (line[cursor] == '[' && cursor + 1 < line.Length && line[cursor + 1] == '[')
            {
                var close = line.IndexOf("]]", cursor + 2, StringComparison.Ordinal);
                cursor = close < 0 ? line.Length : close + 1;
                continue;
            }

            if (!IsIdentifierAt(line, cursor, "require"))
                continue;

            var argStart = cursor + "require".Length;
            while (argStart < line.Length && char.IsWhiteSpace(line[argStart]))
                argStart++;
            if (argStart < line.Length && line[argStart] == '(')
            {
                argStart++;
                while (argStart < line.Length && char.IsWhiteSpace(line[argStart]))
                    argStart++;
            }

            if (argStart >= line.Length || line[argStart] is not ('"' or '\''))
                continue;

            var quote = line[argStart++];
            var nameStart = argStart;
            while (argStart < line.Length)
            {
                if (line[argStart] == '\\' && argStart + 1 < line.Length)
                {
                    argStart += 2;
                    continue;
                }

                if (line[argStart] == quote)
                    break;
                argStart++;
            }

            if (argStart > nameStart)
                yield return (line[nameStart..argStart], nameStart);
            cursor = argStart;
        }
    }

    private static int SkipQuotedLiteral(string line, int start)
    {
        var quote = line[start];
        var cursor = start + 1;
        while (cursor < line.Length)
        {
            if (line[cursor] == '\\' && cursor + 1 < line.Length)
            {
                cursor += 2;
                continue;
            }

            if (line[cursor] == quote)
                return cursor;
            cursor++;
        }

        return line.Length;
    }

    private static bool IsIdentifierAt(string line, int index, string identifier)
    {
        if (index < 0 || index + identifier.Length > line.Length)
            return false;
        if (string.CompareOrdinal(line, index, identifier, 0, identifier.Length) != 0)
            return false;
        if (index > 0 && IsSimpleIdentifierPart(line[index - 1]))
            return false;

        var after = index + identifier.Length;
        return after >= line.Length || !IsSimpleIdentifierPart(line[after]);
    }

    private static bool IsSimpleIdentifierPart(char ch) =>
        ch == '_' || char.IsLetterOrDigit(ch);

    internal static void EmitGoBranchLabelReferences(string preparedLine, Action<string, int> addCallLikeReference)
    {
        foreach (Match match in GoBranchLabelRegex.Matches(preparedLine))
            addCallLikeReference(match.Groups["name"].Value, match.Groups["name"].Index);
    }

    private static void EmitFortranCallReferences(string preparedLine, Action<string, int> addCallLikeReference)
    {
        foreach (Match match in FortranCallRegex.Matches(preparedLine))
            addCallLikeReference(match.Groups["name"].Value, match.Groups["name"].Index);
    }

    private static void EmitPascalCallReferences(string preparedLine, Action<string, int> addCallLikeReference, IReadOnlySet<string>? definitionNames)
    {
        var match = PascalBareCallRegex.Match(preparedLine);
        if (!match.Success)
            return;

        var name = match.Groups["name"].Value;
        if (definitionNames?.Contains(name) == true)
            return;

        addCallLikeReference(name, match.Groups["name"].Index);
    }

    private static void EmitObjCMessageReferences(
        string preparedLine,
        Action<string, int> addCallLikeReference,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (Match match in ObjCMessageRegex.Matches(preparedLine))
        {
            var receiver = match.Groups["receiver"];
            var selector = match.Groups["name"];
            if (char.IsUpper(receiver.Value[0]) && selector.Value is "alloc" or "new")
            {
                ReferenceExtractor.AddReference(references, seen, fileId, receiver.Value, receiver.Index, "instantiate", context, lineNumber, resolveContainerForColumn(receiver.Index));
            }

            addCallLikeReference(selector.Value, selector.Index);
        }

        foreach (Match match in ObjCSelectorRegex.Matches(preparedLine))
            addCallLikeReference(match.Groups["name"].Value.TrimEnd(':'), match.Groups["name"].Index);
    }

    private static void EmitHaskellSpaceCallReferences(string preparedLine, Action<string, int> addCallLikeReference, IReadOnlySet<string>? definitionNames)
    {
        var definitionMatch = HaskellDefinitionRegex.Match(preparedLine);
        var definitionName = definitionMatch.Success ? definitionMatch.Groups["name"].Value : null;
        var scanStart = 0;
        var scanText = preparedLine;
        if (definitionMatch.Success)
        {
            var equalsIndex = preparedLine.IndexOf('=');
            if (equalsIndex >= 0)
            {
                scanStart = equalsIndex + 1;
                scanText = preparedLine[scanStart..];
            }
        }

        foreach (Match match in HaskellSpaceCallRegex.Matches(scanText))
        {
            var name = match.Groups["name"].Value;
            if (definitionNames?.Contains(name) == true || string.Equals(name, definitionName, StringComparison.Ordinal))
                continue;
            addCallLikeReference(name, scanStart + match.Groups["name"].Index);
        }
    }

    private static void EmitElixirParenlessCallReferences(string preparedLine, Action<string, int> addCallLikeReference, IReadOnlySet<string>? definitionNames)
    {
        foreach (Match match in ElixirParenlessCallRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            if (definitionNames?.Contains(name) == true)
                continue;
            addCallLikeReference(name, match.Groups["name"].Index);
        }
    }

    private static void EmitLuaCommandCallReferences(string preparedLine, Action<string, int> addCallLikeReference, IReadOnlySet<string>? definitionNames)
    {
        var match = LuaCommandCallRegex.Match(preparedLine);
        if (!match.Success)
            return;

        var name = LastQualifiedSegment(match.Groups["name"].Value);
        if (definitionNames?.Contains(name) == true)
            return;
        addCallLikeReference(name, match.Groups["name"].Index + match.Groups["name"].Value.LastIndexOf(name, StringComparison.Ordinal));
    }

    private static void EmitSmalltalkMessageReferences(string preparedLine, Action<string, int> addCallLikeReference, IReadOnlySet<string>? definitionNames)
    {
        var definitionMatch = SmalltalkMethodDefinitionRegex.Match(preparedLine);
        if (definitionMatch.Success || SmalltalkClassDeclarationRegex.IsMatch(preparedLine))
            return;

        var consumedUntil = 0;
        foreach (Match match in SmalltalkMessageSendRegex.Matches(preparedLine))
        {
            if (match.Index < consumedUntil)
                continue;

            var selectorGroup = match.Groups["selector"];
            var name = ReadSmalltalkSelector(preparedLine, selectorGroup.Index, out var selectorEndIndex);
            consumedUntil = Math.Max(consumedUntil, selectorEndIndex);
            if (definitionNames?.Contains(name) == true)
                continue;
            addCallLikeReference(name, selectorGroup.Index);
        }
    }

    private static string ReadSmalltalkSelector(string line, int selectorIndex, out int endIndex)
    {
        if (!TryReadSmalltalkSelectorPart(line, selectorIndex, out var firstPart, out var cursor))
        {
            endIndex = selectorIndex;
            return string.Empty;
        }

        if (!firstPart.EndsWith(':'))
        {
            endIndex = cursor;
            return firstPart;
        }

        var selector = firstPart;
        while (true)
        {
            var argumentStart = SkipWhitespace(line, cursor);
            if (argumentStart >= line.Length || !IsIdentifierStart(line[argumentStart]))
                break;

            var argumentEnd = argumentStart + 1;
            while (argumentEnd < line.Length && IsSimpleIdentifierPart(line[argumentEnd]))
                argumentEnd++;

            var nextSelectorStart = SkipWhitespace(line, argumentEnd);
            if (!TryReadSmalltalkSelectorPart(line, nextSelectorStart, out var nextPart, out var nextEnd)
                || !nextPart.EndsWith(':'))
            {
                break;
            }

            selector += nextPart;
            cursor = nextEnd;
        }

        endIndex = cursor;
        return selector;
    }

    private static bool TryReadSmalltalkSelectorPart(string line, int start, out string part, out int end)
    {
        part = string.Empty;
        end = start;
        if (start >= line.Length || !IsIdentifierStart(line[start]))
            return false;

        end = start + 1;
        while (end < line.Length && IsSimpleIdentifierPart(line[end]))
            end++;
        if (end < line.Length && line[end] == ':')
            end++;

        part = line[start..end];
        return true;
    }

    private static void EmitGoFunctionSignatureTypes(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var firstParen = preparedLine.IndexOf('(');
        if (firstParen < 0)
            return;

        var parameterOpen = firstParen;
        var functionHeaderStart = GoFuncRegex.Match(preparedLine).Length;
        var receiverClose = ReferenceExtractor.FindMatchingChar(preparedLine, firstParen, '(', ')');
        if (receiverClose >= 0)
        {
            var afterReceiver = receiverClose + 1;
            while (afterReceiver < preparedLine.Length && char.IsWhiteSpace(preparedLine[afterReceiver]))
                afterReceiver++;
            if (afterReceiver < preparedLine.Length && IsIdentifierStart(preparedLine[afterReceiver]))
            {
                var nextParen = preparedLine.IndexOf('(', afterReceiver);
                if (nextParen > afterReceiver)
                {
                    EmitGoParameterListTypes(preparedLine, firstParen + 1, receiverClose, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
                    parameterOpen = nextParen;
                    functionHeaderStart = afterReceiver;
                }
            }
        }

        var parameterClose = ReferenceExtractor.FindMatchingChar(preparedLine, parameterOpen, '(', ')');
        if (parameterClose < 0)
            return;

        EmitGoTypeParameterConstraints(preparedLine, functionHeaderStart, parameterOpen, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoParameterListTypes(preparedLine, parameterOpen + 1, parameterClose, references, seen, fileId, context, lineNumber, resolveContainerForColumn);

        var returnStart = parameterClose + 1;
        while (returnStart < preparedLine.Length && char.IsWhiteSpace(preparedLine[returnStart]))
            returnStart++;
        if (returnStart >= preparedLine.Length || preparedLine[returnStart] == '{')
            return;

        if (preparedLine[returnStart] == '(')
        {
            var returnClose = ReferenceExtractor.FindMatchingChar(preparedLine, returnStart, '(', ')');
            if (returnClose > returnStart)
                EmitGoParameterListTypes(preparedLine, returnStart + 1, returnClose, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
            return;
        }

        var returnEnd = returnStart;
        while (returnEnd < preparedLine.Length && preparedLine[returnEnd] != '{')
            returnEnd++;

        var expression = preparedLine[returnStart..returnEnd].TrimEnd();
        EmitGoTypeExpression(expression, returnStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitGoInterfaceMethodSignatureTypes(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var nameStart = SkipWhitespace(preparedLine, 0);
        if (nameStart >= preparedLine.Length || !IsIdentifierStart(preparedLine[nameStart]))
            return;

        var nameEnd = nameStart + 1;
        while (nameEnd < preparedLine.Length && IsSimpleIdentifierPart(preparedLine[nameEnd]))
            nameEnd++;

        if (IsGoStatementKeyword(preparedLine[nameStart..nameEnd]))
            return;

        var open = SkipWhitespace(preparedLine, nameEnd);
        if (open >= preparedLine.Length || preparedLine[open] != '(')
            return;

        var close = ReferenceExtractor.FindMatchingChar(preparedLine, open, '(', ')');
        if (close < 0)
            return;

        var returnStart = SkipWhitespace(preparedLine, close + 1);
        if (!IsGoSignatureReturnStart(preparedLine, returnStart))
            return;

        EmitGoParameterListTypes(preparedLine, open + 1, close, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGoSignatureReturnTypes(preparedLine, close + 1, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static bool IsGoStatementKeyword(string value)
        => value is "break" or "case" or "const" or "continue" or "default" or "defer"
            or "else" or "fallthrough" or "for" or "func" or "go" or "goto" or "if"
            or "import" or "package" or "range" or "return" or "select" or "switch"
            or "type" or "var";

    private static bool IsGoSignatureReturnStart(string line, int index)
    {
        if (index >= line.Length)
            return false;

        return line[index] == '(' || IsGoTypeExpressionStart(line, index);
    }

    private static bool IsGoTypeExpressionStart(string line, int index)
    {
        if (index >= line.Length)
            return false;

        return line[index] is '*' or '[' or '~' or '<' || IsIdentifierStart(line[index]);
    }

    private static int FindGoInlineTypeExpressionEnd(string line, int start)
    {
        var squareDepth = 0;
        var parenDepth = 0;
        for (var cursor = start; cursor < line.Length; cursor++)
        {
            switch (line[cursor])
            {
                case '[':
                    squareDepth++;
                    break;
                case ']':
                    if (squareDepth > 0)
                        squareDepth--;
                    break;
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    if (parenDepth == 0)
                        return cursor;
                    parenDepth--;
                    break;
                case ',':
                case '{':
                case '`':
                case '=':
                case ';':
                    if (squareDepth == 0 && parenDepth == 0)
                        return cursor;
                    break;
            }
        }

        return line.Length;
    }

    private static void EmitGoSignatureReturnTypes(
        string preparedLine,
        int start,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var returnStart = SkipWhitespace(preparedLine, start);
        if (returnStart >= preparedLine.Length || preparedLine[returnStart] == '{')
            return;

        if (preparedLine[returnStart] == '(')
        {
            var returnClose = ReferenceExtractor.FindMatchingChar(preparedLine, returnStart, '(', ')');
            if (returnClose > returnStart)
                EmitGoParameterListTypes(preparedLine, returnStart + 1, returnClose, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
            return;
        }

        var returnEnd = returnStart;
        while (returnEnd < preparedLine.Length && preparedLine[returnEnd] != '{')
            returnEnd++;

        var expression = preparedLine[returnStart..returnEnd].TrimEnd();
        EmitGoTypeExpression(expression, returnStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitGoTypeParameterConstraints(
        string line,
        int searchStart,
        int searchEnd,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        if (searchStart < 0 || searchStart >= searchEnd || searchEnd > line.Length)
            return;

        var open = line.IndexOf('[', searchStart, searchEnd - searchStart);
        if (open < 0)
            return;

        var close = ReferenceExtractor.FindMatchingChar(line, open, '[', ']');
        if (close < 0 || close > searchEnd)
            return;

        var list = line[(open + 1)..close];
        foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(list))
        {
            var rawSegment = list.Substring(segmentStart, segmentLength);
            var fragment = rawSegment.Trim();
            if (fragment.Length == 0)
                continue;

            var constraintStart = FirstGoTypeParameterConstraintStart(fragment);
            if (constraintStart < 0)
                continue;

            var fragmentTrimStart = rawSegment.IndexOf(fragment, StringComparison.Ordinal);
            var absoluteStart = open + 1 + segmentStart + Math.Max(0, fragmentTrimStart) + constraintStart;
            EmitGoTypeExpression(fragment[constraintStart..], absoluteStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }
    }

    private static int FirstGoTypeParameterConstraintStart(string fragment)
    {
        var cursor = 0;
        while (cursor < fragment.Length && char.IsWhiteSpace(fragment[cursor]))
            cursor++;
        if (cursor >= fragment.Length || !IsIdentifierStart(fragment[cursor]))
            return -1;

        cursor++;
        while (cursor < fragment.Length && IsSimpleIdentifierPart(fragment[cursor]))
            cursor++;

        var constraintStart = cursor;
        while (constraintStart < fragment.Length && char.IsWhiteSpace(fragment[constraintStart]))
            constraintStart++;

        return constraintStart < fragment.Length ? constraintStart : -1;
    }

    private static void EmitGoParameterListTypes(
        string line,
        int start,
        int end,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        if (end <= start)
            return;

        var list = line[start..end];
        var pendingSingleExpressions = new List<(string Expression, int AbsoluteStart)>();
        foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(list))
        {
            var rawSegment = list.Substring(segmentStart, segmentLength);
            var fragment = rawSegment.Trim();
            if (fragment.Length == 0)
                continue;

            var fragmentTrimStart = rawSegment.IndexOf(fragment, StringComparison.Ordinal);
            var absoluteFragmentStart = start + segmentStart + Math.Max(0, fragmentTrimStart);
            var typeStartInFragment = LastWhitespaceSeparatedTokenStart(fragment);
            if (typeStartInFragment < 0)
                continue;
            if (typeStartInFragment == 0)
            {
                pendingSingleExpressions.Add((fragment, absoluteFragmentStart));
                continue;
            }

            pendingSingleExpressions.Clear();
            var expression = fragment[typeStartInFragment..];
            var absoluteStart = absoluteFragmentStart + typeStartInFragment;
            EmitGoTypeExpression(expression, absoluteStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }

        foreach (var (expression, absoluteStart) in pendingSingleExpressions)
            EmitGoTypeExpression(expression, absoluteStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitGoTypeExpression(
        string expression,
        int start,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var normalized = expression.Trim();
        var leading = expression.IndexOf(normalized, StringComparison.Ordinal);
        if (normalized.Length == 0)
            return;
        var absoluteStart = start + Math.Max(0, leading);
        ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, normalized, absoluteStart, context, lineNumber, resolveContainerForColumn(absoluteStart), "go");
    }

    private static void EmitCommaSeparatedNames(
        string list,
        int listStart,
        string language,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(list))
        {
            var raw = list.Substring(segmentStart, segmentLength).Trim();
            if (raw.Length == 0)
                continue;
            var name = raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? raw;
            var offset = list.IndexOf(name, segmentStart, StringComparison.Ordinal);
            if (offset < 0)
                offset = segmentStart;
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, name, listStart + offset, context, lineNumber, container, language);
        }
    }

    private static string StripCppAccessPrefix(string value)
    {
        var text = value.Trim();
        bool removed;
        do
        {
            removed = false;
            foreach (var prefix in new[] { "public ", "private ", "protected ", "virtual " })
            {
                if (text.StartsWith(prefix, StringComparison.Ordinal))
                {
                    text = text[prefix.Length..].TrimStart();
                    removed = true;
                }
            }
        } while (removed);

        return text;
    }

    private static string LastCppQualifiedSegment(string value)
    {
        var text = value.Trim();
        var genericIndex = text.IndexOf('<');
        if (genericIndex >= 0)
            text = text[..genericIndex].TrimEnd();
        var separator = text.LastIndexOf("::", StringComparison.Ordinal);
        return separator >= 0 ? text[(separator + 2)..].Trim() : text;
    }

    private static string LastQualifiedSegment(string value)
    {
        var dot = value.LastIndexOf('.');
        return dot >= 0 && dot + 1 < value.Length ? value[(dot + 1)..] : value;
    }

    private static string LastPathSegment(string value)
    {
        var slash = value.LastIndexOf('/');
        return slash >= 0 && slash + 1 < value.Length ? value[(slash + 1)..] : value;
    }

    private static int LastWhitespaceSeparatedTokenStart(string value)
    {
        var end = value.Length - 1;
        while (end >= 0 && char.IsWhiteSpace(value[end]))
            end--;
        if (end < 0)
            return -1;

        var start = end;
        while (start >= 0 && !char.IsWhiteSpace(value[start]))
            start--;
        return start + 1;
    }

    private static IEnumerable<Match> EnumerateMatches(Regex regex, string input)
    {
        foreach (Match match in regex.Matches(input))
            yield return match;
    }

    private static void MaskRange(char[] chars, int start, int end)
    {
        for (var i = start; i < end && i < chars.Length; i++)
            chars[i] = ' ';
    }

    private static int SkipWhitespace(string line, int start)
    {
        while (start < line.Length && char.IsWhiteSpace(line[start]))
            start++;
        return start;
    }

    private static bool IsIdentifierStart(char ch) =>
        ch == '_' || char.IsLetter(ch);
}
