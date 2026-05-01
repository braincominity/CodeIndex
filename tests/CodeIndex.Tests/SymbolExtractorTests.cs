using System.Diagnostics;
using CodeIndex.Indexer;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for SymbolExtractor.
/// SymbolExtractorのテスト。
/// </summary>
public class SymbolExtractorTests
{
    [Fact]
    public void Extract_Python_DetectsFunctions()
    {
        // Should detect both sync and async functions
        // 同期・非同期関数を検出する
        var content = "def authenticate(user):\n    pass\nasync def fetch_data():\n    pass";
        var symbols = SymbolExtractor.Extract(1, "python", content);

        Assert.Equal(2, symbols.Count);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "authenticate");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "fetch_data");
    }

    [Fact]
    public void Extract_Python_DetectsClasses()
    {
        var content = "class UserService:\n    pass";
        var symbols = SymbolExtractor.Extract(1, "python", content);

        Assert.Single(symbols);
        Assert.Equal("class", symbols[0].Kind);
        Assert.Equal("UserService", symbols[0].Name);
    }

    [Fact]
    public void Extract_Python_DetectsGenericFunctionsAndTypeAliases()
    {
        var content = """
            type Vector = list[float]
            type Connection = str | int

            def first[T](items: list[T]) -> T:
                return items[0]

            async def fetch_all[T](items: list[T]) -> list[T]:
                return items

            class Stack[T]:
                def push(self, value: T) -> None:
                    pass

            class Config:
                type Theme = str
                type = 5
                type(x)
            """;

        var symbols = SymbolExtractor.Extract(1, "python", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "fetch_all");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Stack");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Vector");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Connection");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Theme" && s.ContainerName == "Config");
        Assert.DoesNotContain(symbols, s => s.Name == "type");
    }

    [Fact]
    public void Extract_Python_DetectsDecoratedAndDunderMethods()
    {
        var content = "@dataclass\nclass User:\n    name: str\n    age: int\n\n    def __init__(self, name: str, age: int) -> None:\n        self.name = name\n\n    @property\n    def display_name(self) -> str:\n        return self.name\n\n    def __str__(self) -> str:\n        return self.name\n\n    @staticmethod\n    def create(name: str) -> 'User':\n        return User(name, 0)";
        var symbols = SymbolExtractor.Extract(1, "python", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "__init__");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "display_name");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "__str__");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "create");
    }

    [Fact]
    public void Extract_Protobuf_DetectsEnumPackageOneofExtendAndService()
    {
        var content = """
            syntax = "proto3";

            package google.api;

            message IssueDetails {
              enum Severity {
                SEVERITY_UNSPECIFIED = 0;
                DEPRECATION = 1;
              }

              oneof kind {
                string name = 1;
                int32 code = 2;
              }
            }

            extend google.protobuf.FieldOptions {
              string custom = 50001;
            }

            service Greeter {
              rpc SayHello (HelloRequest) returns (HelloReply);
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "protobuf", content);

        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "google.api");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "IssueDetails");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Severity" && s.ContainerName == "IssueDetails");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "kind" && s.ContainerName == "IssueDetails");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "google.protobuf.FieldOptions");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Greeter");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "SayHello");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Severity" && s.ContainerName == "IssueDetails");
    }

    [Fact]
    public void Extract_JavaScript_DetectsFunctionsAndClasses()
    {
        // Should detect exported and non-exported functions and classes
        // export有無にかかわらず関数とクラスを検出する
        var content = "export function login() {}\nclass AuthService {}\nimport React from 'react'";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "login");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "AuthService");
        Assert.Contains(symbols, s => s.Kind == "import");
    }

    [Fact]
    public void Extract_VueScriptSetup_DetectsTypeScriptSymbols()
    {
        const string content = """
            <script setup lang="ts">
            import { computed, ref } from "vue";

            const count = ref(0);
            const doubled = computed(() => count.value * 2);

            function increment() {
                count.value++;
            }
            </script>

            <template>
                <button @click="increment">{{ count }} / {{ doubled }}</button>
            </template>
            """;

        var symbols = SymbolExtractor.Extract(1, "vue", content);

        Assert.Contains(symbols, symbol => symbol.Kind == "function" && symbol.Name == "increment");
        Assert.Contains(symbols, symbol => symbol.Kind == "import" && symbol.Name.Contains("computed", StringComparison.Ordinal));
        Assert.Contains(SymbolExtractor.GetSupportedLanguages(), lang => lang == "vue");
    }

    [Fact]
    public void Extract_SvelteScript_DetectsTypeScriptSymbolsAndReactiveProperty()
    {
        const string content = """
            <script lang="ts">
                let count = 0;
                $: doubled = count * 2;

                function increment() {
                    count++;
                }
            </script>

            <button on:click={increment}>{count} / {doubled}</button>
            """;

        var symbols = SymbolExtractor.Extract(1, "svelte", content);

        Assert.Contains(symbols, symbol => symbol.Kind == "function" && symbol.Name == "increment");
        Assert.Contains(symbols, symbol => symbol.Kind == "property" && symbol.Name == "doubled");
        Assert.Contains(SymbolExtractor.GetSupportedLanguages(), lang => lang == "svelte");
    }

    [Fact]
    public void Extract_Go_DetectsSingleLineAndGroupedImports()
    {
        var content = """
            package demo

            import "bytes" // ERROR "invalid import path (empty string)"
            import alias "fmt" // trailing comment

            import (
                "io"
                . "strings"
                _ "net/http/pprof"
                alias2 "example.com/project"
                // ignored comment
            )

            func main() {}
            """;

        var symbols = SymbolExtractor.Extract(1, "go", content);
        var imports = symbols.Where(s => s.Kind == "import").ToList();

        Assert.Equal(6, imports.Count);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "main");
        Assert.Contains(imports, s => s.Name == "\"bytes\"");
        Assert.Contains(imports, s => s.Name == "alias \"fmt\"");
        Assert.Contains(imports, s => s.Name == "\"io\"");
        Assert.Contains(imports, s => s.Name == ". \"strings\"");
        Assert.Contains(imports, s => s.Name == "_ \"net/http/pprof\"");
        Assert.Contains(imports, s => s.Name == "alias2 \"example.com/project\"");
        Assert.DoesNotContain(imports, s => s.Name == "(");
        Assert.DoesNotContain(imports, s => s.Name.Contains("ERROR", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_Go_DetectsTopLevelConstsAndVarsAsProperties()
    {
        var content = """
            package demo

            const MaxRetries = 3
            const Timeout int = 30
            const (
                StatusActive = "active"
            )

            var ErrNotFound = errors.New("not found")
            var DefaultConfig *Config = &Config{}

            func build() {
                user := User{Name: "alice"}
                _ = user
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "go", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "MaxRetries");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Timeout");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "StatusActive");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "ErrNotFound");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "DefaultConfig");
        Assert.DoesNotContain(symbols, s => s.Name == "Name");
    }

    [Fact]
    public void Extract_Cpp_DetectsQualifiedDefinitionsConceptsAndModules()
    {
        var content = """
            export module my_module;

            inline namespace v2 {}

            template <typename T>
            concept Addable = requires(T a, T b) { a + b; };

            class Foo {
            public:
                Foo();
                ~Foo();
                void bar();
                Foo& operator=(const Foo&);
                operator int() const;
                static int counter;
            };

            Foo::Foo() {}
            Foo::~Foo() {}
            void Foo::bar() {}
            Foo& Foo::operator=(const Foo&) { return *this; }
            Foo::operator int() const { return 0; }
            int Foo::counter = 0;

            template <typename T>
            T add(T a, T b) { return a + b; }

            template <>
            int add<int>(int a, int b) { return a + b + 1; }

            class Outer {
            public:
                class Inner {
                public:
                    void method();
                };
            };

            void Outer::Inner::method() {}
            """;

        var symbols = SymbolExtractor.Extract(1, "cpp", content);

        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "my_module");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "v2");
        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "Addable");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Foo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Foo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "~Foo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "bar");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "operator=");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "operator int");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "counter");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "add");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "method");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "int");
    }

    [Fact]
    public void Extract_JavaScript_DetectsQualifiedAssignmentsAndObjectLiteralFunctionValues()
    {
        var content = """
            function Vehicle(make) { this.make = make; }
            Vehicle.prototype.start = function start() { return this.make; };
            Vehicle.factory = function factory() { return new Vehicle("default"); };
            Vehicle.VERSION = "1.0";

            var Foo = {};
            Foo.Bar = class {
                method() { return 1; }
            };

            var MyNS = { utils: {} };
            MyNS.utils.parse = function parse(s) { return s; };

            var add = function add(a, b) { return a + b; };

            const handlers = {
                onClick() { return true; },
                onSubmit: function submitHandler() { return true; },
                onClose: () => { return true; }
            };
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Vehicle.prototype.start");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Vehicle.factory");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Vehicle.VERSION");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Foo.Bar");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "method" && s.ContainerName == "Foo.Bar");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MyNS.utils.parse");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "add");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "onClick" && s.ContainerName == "handlers");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "onSubmit" && s.ContainerName == "handlers");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "onClose" && s.ContainerName == "handlers");
    }

    [Fact]
    public void Extract_JavaScript_DetectsHocWrappedComponentBindings()
    {
        // HOC-wrapped / call-result / tagged-template component bindings must not be
        // silently dropped — every PascalCase `const Name = <non-arrow RHS>` shape
        // should produce a `function` symbol so that `definition`, `callers`,
        // `inspect`, and default exports can resolve the name. Closes #240.
        // React.memo / React.forwardRef / React.lazy / connect(...)(...) /
        // styled.div`...` / withAuth(Home) のような HOC ラップと呼び出し結果・
        // タグ付きテンプレート代入の束縛が漏れないことを確認する。どの PascalCase
        // 名 `const Name = <非 arrow の RHS>` も `function` シンボルになることで、
        // `definition` / `callers` / `inspect` と default export が名前解決できる。
        // Closes #240.
        var content = """
            import React from 'react';

            const Box = React.forwardRef(function Box(props, ref) {
                return React.createElement('div', { ref }, props.children);
            });

            const Expensive = React.memo(function Expensive({ data }) {
                return React.createElement('div', null, data);
            });

            const Wrapped = React.memo(() => React.createElement('div', null, 'arrow'));

            const Lazy = React.lazy(() => import('./X'));

            const Connected = connect(mapState)(MyComponent);

            const Styled = styled.div`color: red`;

            const WithAuth = withAuthentication(Home);
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Box");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Expensive");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Wrapped");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Lazy");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Connected");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Styled");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "WithAuth");
    }

    [Fact]
    public void Extract_JavaScript_HocBindingPatternSkipsLowercaseBindings()
    {
        // Lowercase names are NOT captured by the HOC/call-result binding row so
        // ordinary non-component constants (`const count = 5;`, `const total = sum(a, b);`)
        // do not gain phantom `function` rows. The capitalization gate also keeps
        // every non-arrow ordinary constant out of the symbol table; only the
        // PascalCase shape — which matches the React / component naming
        // convention — is surfaced. Closes #240.
        // 小文字始まり名は HOC / 呼び出し結果束縛パターンで取り込まれない。通常の
        // 非コンポーネント定数（`const count = 5;`、`const total = sum(a, b);`）に
        // 架空の `function` 行が生えないことを確認する。大文字始まりのゲートにより、
        // React / コンポーネント命名規則に沿う PascalCase 形だけがシンボルテーブル
        // に出る。Closes #240.
        var content = """
            const count = 5;
            const total = sum(a, b);
            const config = loadConfig();
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "count");
        Assert.DoesNotContain(symbols, s => s.Name == "total");
        Assert.DoesNotContain(symbols, s => s.Name == "config");
    }

    [Fact]
    public void Extract_JavaScript_HocBindingPatternDoesNotShadowCapitalizedArrow()
    {
        // A capitalized arrow-function binding (`const Foo = () => <div/>`) still
        // matches the pre-existing arrow pattern, which runs FIRST and sets the
        // same-line stop flag. The new HOC row must not produce a second,
        // BodyStyle.None duplicate for the same symbol — the arrow row already
        // captures the function with a brace body range. Closes #240.
        // 大文字始まりの arrow 関数束縛（`const Foo = () => <div/>`）は既存 arrow
        // パターンに先行一致し、同一行の stop flag が立つ。今回の HOC 行で重複
        // シンボル（BodyStyle.None の `function Foo`）を追加で生やしてはいけない —
        // arrow 行がすでに本体範囲つきで捕捉している。Closes #240.
        var content = """
            const Foo = () => {
                return 1;
            };
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        // Assert.Single already guarantees no duplicate was emitted by the new HOC
        // row on the same line — the arrow row still wins via stopAfterFirstPatternMatch.
        // Assert.Single で、新 HOC 行が同一行に重複シンボルを生やしていないことを保証する。
        // arrow 行が stopAfterFirstPatternMatch で先勝ちしている。
        var foo = Assert.Single(symbols.Where(s => s.Name == "Foo"));
        Assert.Equal("function", foo.Kind);
        // Arrow pattern uses BodyStyle.Brace, so EndLine is advanced past StartLine
        // when the body spans multiple lines. The HOC row (BodyStyle.None) would
        // leave EndLine equal to StartLine; a strictly greater end line proves the
        // arrow row won.
        // arrow パターンは BodyStyle.Brace なので、本体が複数行の場合 EndLine は
        // StartLine より後まで伸びる。HOC 行（BodyStyle.None）は EndLine を
        // StartLine のまま残すため、StartLine より大きい EndLine は arrow 行が
        // 勝ったことの証拠になる。
        Assert.True(foo.EndLine > foo.StartLine);
    }

    [Fact]
    public void Extract_TypeScript_DetectsHocWrappedComponentBindingsWithTypeAnnotation()
    {
        // TypeScript HOC bindings frequently carry an explicit type annotation
        // between the name and `=`; the TypeScript row's optional `:` branch
        // must consume the annotation so the name is still captured. Closes #240.
        // TypeScript の HOC 束縛では名前と `=` の間に型注釈が入ることが多い。
        // TypeScript 行のオプションの `:` 分岐が型注釈を消費し、名前が正しく
        // 取得できることを確認する。Closes #240.
        var content = """
            import React from 'react';

            const Connected: React.ComponentType<Props> = connect(mapState)(MyComponent);

            const Styled: StyledComponent<'div', Theme> = styled.div`color: red`;

            const Callback: (x: number) => number = (x) => x + 1;
            """;

        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Connected");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Styled");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Callback");
    }

    [Fact]
    public void Extract_TypeScript_HocBindingPatternAcceptsGenericTypeArgumentsOnReactHoc()
    {
        // TypeScript HOCs very frequently carry type arguments directly on the
        // HOC call itself — `React.forwardRef<HTMLDivElement, Props>(...)`,
        // `React.memo<Props>(...)`, `React.lazy<typeof X>(...)`, and the same
        // shape on bare `forwardRef<T>(...)` / `memo<T>(...)` / `lazy<T>(...)` /
        // `connect<State, Dispatch>(...)` / `observer<Props>(...)` / any
        // `with<Pascal><T>(...)` call. The narrow HOC allowlist must still
        // accept them; the earlier revision dropped every generic shape because
        // the `<...>` tokens pushed the `(` away from the HOC name. Closes #240.
        // TypeScript の HOC には HOC 呼び出し自身に型引数が付く形が非常に多い
        // — `React.forwardRef<HTMLDivElement, Props>(...)`、`React.memo<Props>(...)`、
        // `React.lazy<typeof X>(...)`、素の `forwardRef<T>(...)` /
        // `memo<T>(...)` / `lazy<T>(...)` / `connect<State, Dispatch>(...)` /
        // `observer<Props>(...)` / `with<Pascal><T>(...)` のいずれも同じ形。
        // narrow な HOC allowlist でこの形を落としてはいけない。以前のリビジョンは
        // `<...>` トークンが `(` を HOC 名から離してしまい、generic 形が全部
        // 落ちていた。Closes #240.
        var content = """
            import React from 'react';

            const Box = React.forwardRef<HTMLDivElement, Props>((props, ref) => <div ref={ref} />);
            const MemoBox = React.memo<Props>(Box);
            const LazyBox = React.lazy<typeof Box>(() => import('./Box'));

            const BareBox = forwardRef<HTMLDivElement, Props>((props, ref) => <div ref={ref} />);
            const BareMemo = memo<Props>(BareBox);
            const BareLazy = lazy<typeof BareBox>(() => import('./BareBox'));

            const ConnectedGeneric = connect<State, Dispatch>(mapState, mapDispatch)(MyComponent);
            const Observed = observer<Props>(MyComponent);
            const WithAuthGeneric = withAuthentication<Props>(Home);

            const NestedMemo = React.memo<Map<string, Props>>(Box);
            const FunctionTypeMemo = React.memo<(props: Props) => JSX.Element>(Box);
            const DeepNestedMemo = React.memo<Record<string, Map<string, Props>>>(Box);
            """;

        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Box");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MemoBox");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "LazyBox");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "BareBox");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "BareMemo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "BareLazy");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ConnectedGeneric");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Observed");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "WithAuthGeneric");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "NestedMemo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "FunctionTypeMemo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "DeepNestedMemo");
    }

    [Fact]
    public void Extract_TypeScript_DetectsAmbientDeclarationsTypeAliasesAndDecoratedClassMembers()
    {
        var content = """
            type Handler = (e: Event) => void;
            type Coord = { x: number; y: number };

            declare function globalHelper(x: number): number;
            declare const VERSION: string;

            declare module "ext" {
                export function loadExt(): void;
            }

            class Api {
                @log fetch() {}
                @readonly name = "x";
            }

            function log(_: any, _k: string) {}
            function readonly(_: any, _k: string) {}
            """;

        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Handler");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Coord");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "globalHelper");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "VERSION");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "loadExt" && s.ContainerName == "ext");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Api");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "fetch" && s.ContainerName == "Api");
    }

    [Fact]
    public void Extract_TypeScript_DetectsInterfaceAbstractMembersAndTypedArrowAssignments()
    {
        var content = """
            interface User {
              id: string;
              name: string;
              readonly age: number;
              login(password: string): Promise<boolean>;
              logout(): void;
            }

            interface Callback<T> {
              (data: T): void;
              timeout?: number;
            }

            abstract class Base {
              abstract compute(): number;
              abstract readonly count: number;
              protected ready(): boolean { return true; }
            }

            interface Props { title: string; }
            const Header: React.FC<Props> = ({ title }) => <h1>{title}</h1>;
            const add: (a: number, b: number) => number = (a, b) => a + b;
            const handler: MouseEventHandler = (e) => { e.preventDefault(); };
            """;

        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "Callback");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Base");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "id" && s.ContainerName == "User");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "age" && s.ContainerName == "User");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "login" && s.ContainerName == "User");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "logout" && s.ContainerName == "User");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "compute" && s.ContainerName == "Base");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "count" && s.ContainerName == "Base");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Header");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "add");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "handler");
    }

    [Fact]
    public void Extract_TypeScript_CollapsesFunctionOverloadsIntoImplementationRow()
    {
        var content = """
            function format(x: number): string;
            function format(x: string): string;
            function format(x: number | string): string {
                return String(x);
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "typescript", content);
        var overloads = symbols.Where(s => s.Kind == "function" && s.Name == "format").ToList();

        var implementation = Assert.Single(overloads);
        Assert.NotNull(implementation.BodyStartLine);
        Assert.NotNull(implementation.BodyEndLine);
        Assert.True(implementation.EndLine > implementation.StartLine);
    }

    [Fact]
    public void Extract_JavaScript_HocBindingPatternSkipsPascalCaseNonHocConstants()
    {
        // PascalCase bindings whose RHS is NOT a known HOC prefix must not be
        // silently promoted to `function` symbols — that would create phantom
        // symbol rows and pollute `definition`, `symbols`, and `inspect` output.
        // The narrow HOC-prefix gate (React.memo/React.forwardRef/React.lazy
        // only — bare `React.` is NOT accepted — styled/connect/memo/forwardRef/
        // lazy/observer/with<Pascal>) intentionally rejects ordinary constants,
        // ALL_CAPS config values, and arbitrary call results. Closes #240.
        // RHS が既知の HOC プレフィックスでない PascalCase / ALL_CAPS 束縛は
        // `function` シンボルに昇格させてはいけない — 架空のシンボル行が出ると
        // `definition` / `symbols` / `inspect` が汚染される。狭い HOC プレフィックス
        // ゲート（`React.memo` / `React.forwardRef` / `React.lazy` のみで、素の
        // `React.` は受け付けない。`styled` / `connect` / `memo` / `forwardRef` /
        // `lazy` / `observer` / `with<Pascal>`）で通常定数、ALL_CAPS 設定値、任意の
        // 呼び出し結果を意図的に弾く。Closes #240.
        var content = """
            const Config = loadConfig();
            const ENV = process.env.NODE_ENV;
            const API = 'https://example.com';
            const Total = sum(a, b);
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "Config");
        Assert.DoesNotContain(symbols, s => s.Name == "ENV");
        Assert.DoesNotContain(symbols, s => s.Name == "API");
        Assert.DoesNotContain(symbols, s => s.Name == "Total");
    }

    [Fact]
    public void Extract_JavaScript_HocBindingPatternDoesNotCollideWithClassExpressionBinding()
    {
        // `const Widget = class extends React.Component {}` is a class-expression
        // binding. It must produce a single `class Widget` symbol (from the
        // synthetic class-expression pass), NOT both `function Widget` (from the
        // HOC row) and `class Widget`. The narrow HOC-prefix regex excludes
        // `= class` so the two passes do not collide. Closes #240.
        // `const Widget = class extends React.Component {}` はクラス式束縛。
        // `class Widget`（class expression 合成パス由来）だけが出るべきで、
        // `function Widget`（HOC 行由来）と `class Widget` が二重に出てはいけない。
        // 狭い HOC プレフィックス正規表現は `= class` を受け付けないため、2 つの
        // パスが衝突しない。Closes #240.
        var content = """
            const Widget = class extends React.Component {
                render() { return null; }
            };
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        var widgetSymbols = symbols.Where(s => s.Name == "Widget").ToList();
        Assert.Single(widgetSymbols);
        Assert.Equal("class", widgetSymbols[0].Kind);
    }

    [Fact]
    public void Extract_TypeScript_TypedArrowBindingPreservesBraceBody()
    {
        // A TypeScript arrow-function binding with an explicit function-type
        // annotation (`const Callback: (x: number) => number = (x) => {...}`)
        // must match the arrow row (BodyStyle.Brace) and keep its multi-line
        // body range, even though the type annotation itself contains `=>`.
        // The HOC row with BodyStyle.None must NOT shadow it. Closes #240.
        // 関数型注釈付き TypeScript arrow 束縛（`const Callback: (x: number) =>
        // number = (x) => {...}`）は arrow 行（BodyStyle.Brace）に一致し、型注釈に
        // `=>` が含まれていても複数行の本体範囲が維持される必要がある。
        // BodyStyle.None の HOC 行で上書きされてはいけない。Closes #240.
        var content = """
            const Callback: (x: number) => number = (x) => {
                return x + 1;
            };
            """;

        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var callback = Assert.Single(symbols.Where(s => s.Name == "Callback"));
        Assert.Equal("function", callback.Kind);
        // Arrow row (BodyStyle.Brace) pushes EndLine past StartLine for a multi-line body.
        // HOC row (BodyStyle.None) would leave EndLine==StartLine.
        // arrow 行は複数行本体で EndLine を StartLine より後ろへ伸ばす。HOC 行
        // （BodyStyle.None）なら EndLine は StartLine のまま残るため、これで
        // arrow 行が勝ったことを確認できる。
        Assert.True(callback.EndLine > callback.StartLine);
    }

    [Fact]
    public void Extract_JavaScript_HocBindingPatternSkipsNonHocReactApiCalls()
    {
        // `React.` on the RHS is not a HOC marker on its own — only
        // `React.memo(` / `React.forwardRef(` / `React.lazy(` are real HOCs.
        // Other React APIs (`React.createContext(...)`, hooks like
        // `React.useCallback(...)` / `React.useMemo(...)`, `React.createRef(...)`)
        // return plain values and must NOT produce phantom `function` symbols.
        // Pins the strict allowlist on both JS and TS sides. Closes #240.
        // RHS の `React.` だけでは HOC ではない — 真の HOC は
        // `React.memo(` / `React.forwardRef(` / `React.lazy(` のみ。それ以外の
        // React API（`React.createContext(...)`、`React.useCallback(...)` / `React.useMemo(...)`
        // などの hooks、`React.createRef(...)`）は素の値を返すだけで、phantom
        // `function` シンボルを生やしてはいけない。JS / TS の両行で厳格な allowlist を
        // pin する。Closes #240.
        var jsContent = """
            const Theme = React.createContext(null);
            const Stable = React.useCallback(() => 1, []);
            const Derived = React.useMemo(() => compute(), [dep]);
            const Ref = React.createRef();
            """;

        var jsSymbols = SymbolExtractor.Extract(1, "javascript", jsContent);

        Assert.DoesNotContain(jsSymbols, s => s.Name == "Theme");
        Assert.DoesNotContain(jsSymbols, s => s.Name == "Stable");
        Assert.DoesNotContain(jsSymbols, s => s.Name == "Derived");
        Assert.DoesNotContain(jsSymbols, s => s.Name == "Ref");

        var tsContent = """
            const Theme = React.createContext<string | null>(null);
            const Stable = React.useCallback(() => 1, []);
            const Derived: number = React.useMemo(() => compute(), [dep]);
            const Ref = React.createRef<HTMLDivElement>();
            """;

        var tsSymbols = SymbolExtractor.Extract(2, "typescript", tsContent);

        Assert.DoesNotContain(tsSymbols, s => s.Name == "Theme");
        Assert.DoesNotContain(tsSymbols, s => s.Name == "Stable");
        Assert.DoesNotContain(tsSymbols, s => s.Name == "Derived");
        Assert.DoesNotContain(tsSymbols, s => s.Name == "Ref");
    }

    [Fact]
    public void Extract_JavaScript_HocBindingPatternDoesNotMatchComparisonShape()
    {
        // In JavaScript, `const Result = memo < Props > (Component);` is a chained
        // comparison / call expression — NOT a HOC binding with generic type
        // arguments. The TypeScript HOC row intentionally accepts an optional
        // `<TypeArgs>` token between the HOC call name and `(`, but the
        // JavaScript row must not, because JS has no generic syntax. A regex
        // that shares the generic token between the two rows would produce
        // phantom `function Result` on pure-JS comparison shapes. Pins the
        // asymmetry so `memo < Props >` / `forwardRef < Props >` /
        // `lazy < typeof X >` / `connect < State, Dispatch >` / `observer < Props >` /
        // `withAuth < Props >` in a JS source stay 0-symbol. Closes #240.
        // JavaScript では `const Result = memo < Props > (Component);` は generic 付きの
        // HOC 束縛ではなく、比較・呼び出しの連鎖式である。TypeScript 行は HOC 呼び出し名と
        // `(` の間に `<TypeArgs>` を意図的に受け入れるが、JavaScript 行は受け入れては
        // いけない。JS に generic 構文は無いため、両行で同じ regex を共有すると純粋な
        // JS の比較式から phantom な `function Result` が生えてしまう。非対称性を pin し、
        // JS ソース上の `memo < Props >` / `forwardRef < Props >` /
        // `lazy < typeof X >` / `connect < State, Dispatch >` / `observer < Props >` /
        // `withAuth < Props >` が 0 シンボルのままであることを保証する。Closes #240.
        var content = """
            const Result = memo < Props > (Component);
            const Forwarded = forwardRef < Props > (Component);
            const Lazied = lazy < typeof Component > (Component);
            const Connected = connect < State, Dispatch > (Component);
            const Observed = observer < Props > (Component);
            const WithAuth = withAuthentication < Props > (Home);
            const ReactMemoed = React.memo < Props > (Component);
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "Result");
        Assert.DoesNotContain(symbols, s => s.Name == "Forwarded");
        Assert.DoesNotContain(symbols, s => s.Name == "Lazied");
        Assert.DoesNotContain(symbols, s => s.Name == "Connected");
        Assert.DoesNotContain(symbols, s => s.Name == "Observed");
        Assert.DoesNotContain(symbols, s => s.Name == "WithAuth");
        Assert.DoesNotContain(symbols, s => s.Name == "ReactMemoed");
    }

    [Fact]
    public void Extract_JavaScript_HocBindingPatternRequiresTaggedTemplateForStyled()
    {
        // The `styled` HOC branch must require a tagged-template backtick on the same
        // line. `const StyledFactory = styled.div;` (factory capture) and
        // `const StyledFactoryCall = styled(Component);` (plain function call) are
        // NOT component bindings — no component is produced on that line — and must
        // stay 0-symbol. Only the tagged-template forms
        // (`styled.div\`...\``, `styled(Component)\`...\``) create a real styled
        // component binding and must still match. Closes #240.
        // `styled` HOC 分岐はタグ付きテンプレートのバッククォートを同一行で必須にする。
        // `const StyledFactory = styled.div;`（factory 捕捉）や
        // `const StyledFactoryCall = styled(Component);`（素の関数呼び出し）はその行で
        // コンポーネントを生成しないため、0 シンボルのままであるべき。
        // タグ付きテンプレート形（`styled.div\`...\``、`styled(Component)\`...\``）のみが
        // 実体のある styled コンポーネント束縛となり、引き続きマッチする。Closes #240.
        var content = """
            const StyledFactory = styled.div;
            const StyledFactoryCall = styled(Component);
            const RealStyled = styled.div`color: red;`;
            const WrappedStyled = styled(Component)`color: blue;`;
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "StyledFactory");
        Assert.DoesNotContain(symbols, s => s.Name == "StyledFactoryCall");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "RealStyled");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "WrappedStyled");
    }

    [Fact]
    public void Extract_TypeScript_HocBindingPatternRequiresTaggedTemplateForStyled()
    {
        // Same tagged-template requirement on the TypeScript side — the factory
        // capture and plain call shapes must not produce phantom bindings even in
        // TS sources, while real tagged templates (including ones with generic
        // type arguments on `styled.div<Props>\`...\``) still match. Closes #240.
        // TypeScript 側でも同じタグ付きテンプレート要件を適用する — factory 捕捉や素の
        // 関数呼び出し形は TS ソース上でも phantom 束縛を生やさず、タグ付きテンプレート
        // （generic 型引数を伴う `styled.div<Props>\`...\`` も含む）のみが引き続き
        // マッチする。Closes #240.
        var content = """
            const StyledFactory = styled.div;
            const StyledFactoryCall = styled(Component);
            const RealStyled = styled.div`color: red;`;
            const TypedStyled = styled.div<Props>`color: blue;`;
            """;

        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "StyledFactory");
        Assert.DoesNotContain(symbols, s => s.Name == "StyledFactoryCall");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "RealStyled");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "TypedStyled");
    }

    [Fact]
    public void Extract_JavaScript_HocBindingPatternRejectsSameLineMultiStatementStyledFactory()
    {
        // A factory-capture or plain-call `styled` binding placed on the same
        // source line as an UNRELATED tagged template (a separate statement after
        // `;`) must still be rejected. The gate scans only between the match end
        // and the next `;`, so the backtick in `const note = \`...\`;` does not
        // re-open the gate for `const StyledFactory = styled.div;` earlier on the
        // same line. Closes #240 follow-up (codex review #7 blocker).
        // factory 捕捉 / 素の呼び出し形の `styled` 束縛と、無関係なタグ付きテンプレート
        // （`;` で区切られた別の文）が同じ行に置かれたケースでも除外は働かなければ
        // ならない。ゲートは match 終端から次の `;` までしか見ないので、行後半の
        // `const note = \`...\`;` が行前半の `const StyledFactory = styled.div;` の
        // ゲートを誤って解除しない。Closes #240 follow-up（codex レビュー #7 の
        // blocker 対応）。
        var content = """
            const StyledFactory = styled.div; const note = `not a component`;
            const StyledFactoryCall = styled(Component); const later = `still not a component`;
            const RealStyled = styled.div`color: red;`;
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "StyledFactory");
        Assert.DoesNotContain(symbols, s => s.Name == "StyledFactoryCall");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "RealStyled");
    }

    [Fact]
    public void Extract_TypeScript_HocBindingPatternRejectsSameLineMultiStatementStyledFactory()
    {
        // Same statement-local gate applies on TypeScript input: a backtick from
        // an unrelated later statement on the same line must not keep a
        // factory-capture / plain-call styled binding alive. Closes #240
        // follow-up (codex review #7 blocker).
        // TypeScript 側でも同じ statement-local ゲートが必要。同じ行上の無関係な
        // タグ付きテンプレートによるバッククォートが、前方の factory 捕捉 / 素の
        // 呼び出し形 styled 束縛を生かしてはいけない。Closes #240 follow-up
        // （codex レビュー #7 の blocker 対応）。
        var content = """
            const StyledFactory = styled.div; const note = `not a component`;
            const StyledFactoryCall = styled(Component); const later = `still not a component`;
            const TypedStyled = styled.div<Props>`color: blue;`;
            """;

        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "StyledFactory");
        Assert.DoesNotContain(symbols, s => s.Name == "StyledFactoryCall");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "TypedStyled");
    }

    [Fact]
    public void Extract_JavaScript_HocBindingPatternRejectsPostTemplateComparisonAndDivisionOperators()
    {
        // After a real tagged template, the styled-factory gate must also
        // reject post-template comparison and division operators. Without this
        // guard, `styled.div` bindings can leak through as phantom symbols for
        // `<`, `>`, and `/` continuations. Closes #997.
        // 本物のタグ付きテンプレートの後でも、styled-factory ゲートは比較演算子と
        // 除算演算子を拒否しなければならない。これがないと `styled.div` 束縛が
        // `<`、`>`、`/` の継続として phantom シンボル化する。Closes #997.
        var content = """
            const StyledLess = styled.div`color: red` < theme;
            const StyledGreater = styled.div`color: red` > theme;
            const StyledDivide = styled.div`color: red` / theme;
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "StyledLess");
        Assert.DoesNotContain(symbols, s => s.Name == "StyledGreater");
        Assert.DoesNotContain(symbols, s => s.Name == "StyledDivide");
    }

    [Fact]
    public void Extract_TypeScript_HocBindingPatternRejectsPostTemplateComparisonAndDivisionOperators()
    {
        // Same post-template operator rejection applies to TypeScript inputs.
        // Comparison and division operators after the closing backtick must not
        // keep a styled binding alive. Closes #997.
        // TypeScript 入力でも同じ post-template 演算子拒否が必要。閉じバッククォート
        // の後に比較演算子・除算演算子があっても styled 束縛を生かしてはならない。
        // Closes #997.
        var content = """
            const StyledLess = styled.div`color: red` < theme;
            const StyledGreater = styled.div`color: red` > theme;
            const StyledDivide = styled.div`color: red` / theme;
            """;

        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "StyledLess");
        Assert.DoesNotContain(symbols, s => s.Name == "StyledGreater");
        Assert.DoesNotContain(symbols, s => s.Name == "StyledDivide");
    }

    [Fact]
    public void Extract_JavaScript_HocBindingPatternIgnoresStyledBacktickInCommentsAndStrings()
    {
        // The statement-local styled-factory gate must understand line comments,
        // block comments, and plain string literals when looking for a backtick
        // or `;`. A backtick or `;` that lives only inside a comment or string
        // must not steer the gate's accept/reject decision. Four failure shapes
        // are pinned:
        //   (a) `// \`...\`` — backtick inside a line comment must not accept
        //       a factory-capture binding.
        //   (b) `/* \`...\` */` — backtick inside a block comment must not
        //       accept a factory-capture binding.
        //   (c) `+ "\`"` — backtick inside a plain string literal must not
        //       accept a non-template binding.
        //   (d) `/* ; */ \`color:red\`;` — `;` inside a block comment must not
        //       fence a real subsequent backtick off from a real tagged
        //       template on the same statement.
        // Closes #240 follow-up (codex review #8 blocker).
        // 文ローカルの styled factory ゲートは、バッククォートや `;` を探索する際に行コメント /
        // ブロックコメント / 通常文字列リテラルを構文として認識する必要がある。コメントや文字列
        // 内のバッククォート・`;` がゲートの判定を誤らせてはならない。(a) 行コメント内の
        // バッククォートで factory 捕捉が維持されない、(b) ブロックコメント内のバッククォートで
        // 維持されない、(c) 文字列リテラル内のバッククォートで維持されない、(d) ブロックコメント
        // 内の `;` によって同一文の本物のバッククォートが文終端で遮られない、の 4 形を pin する。
        // Closes #240 follow-up（codex レビュー #8 の blocker 対応）。
        var content = """
            const LineCommentFactory = styled.div // `not a template`
            const BlockCommentFactory = styled.div /* `not a template` */;
            const StringLiteralFactory = styled.div + "`";
            const RealStyledAfterBlock = styled.div /* ; */ `color:red`;
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "LineCommentFactory");
        Assert.DoesNotContain(symbols, s => s.Name == "BlockCommentFactory");
        Assert.DoesNotContain(symbols, s => s.Name == "StringLiteralFactory");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "RealStyledAfterBlock");
    }

    [Fact]
    public void Extract_TypeScript_HocBindingPatternIgnoresStyledBacktickInCommentsAndStrings()
    {
        // Same comment / string awareness on the TypeScript side. Closes #240
        // follow-up (codex review #8 blocker).
        // TypeScript 側でも同じコメント / 文字列対応が必要。Closes #240 follow-up
        // （codex レビュー #8 の blocker 対応）。
        var content = """
            const LineCommentFactory = styled.div // `not a template`
            const BlockCommentFactory = styled.div /* `not a template` */;
            const StringLiteralFactory = styled.div + "`";
            const RealStyledAfterBlock = styled.div<Props> /* ; */ `color:red`;
            """;

        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "LineCommentFactory");
        Assert.DoesNotContain(symbols, s => s.Name == "BlockCommentFactory");
        Assert.DoesNotContain(symbols, s => s.Name == "StringLiteralFactory");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "RealStyledAfterBlock");
    }

    [Fact]
    public void Extract_JavaScript_HocBindingPatternAcceptsMultiLineTaggedTemplateContinuation()
    {
        // Prettier / dprint style often places the tagged-template backtick on the
        // line after `styled.div` / `styled(Component)`. The statement-local gate
        // must walk across raw lines within a bounded lookahead window so these
        // bindings still register as function symbols. Implicit ASI must still
        // terminate the scan: `const X = styled.div\nconst Y = 5;` stays rejected
        // because the continuation line begins with a `const` statement starter.
        // Closes #240 follow-up (codex review #9 blocker, upstream issue #901).
        // Prettier / dprint の整形では `styled.div` / `styled(Component)` の次行に
        // タグ付きテンプレートのバッククォートを置くことが多い。文ローカルのゲートは
        // 所定の行数まで改行をまたいで走査し、これらの束縛を function シンボルとして
        // 維持しなければならない。同時に暗黙 ASI による終端は守る必要があり、
        // `const X = styled.div\nconst Y = 5;` は継続行が `const` の文頭キーワードで
        // 始まるため引き続き除外される。Closes #240 follow-up（codex レビュー #9 の
        // blocker 対応、上流 issue #901）。
        var content = """
            const MultiLineMember = styled.div
              `color: red;`;
            const MultiLineCall = styled(Component)
              `color: blue;`;
            const AsiRejectedMember = styled.div
            const AsiFollower = 5;
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MultiLineMember");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MultiLineCall");
        Assert.DoesNotContain(symbols, s => s.Name == "AsiRejectedMember");
    }

    [Fact]
    public void Extract_TypeScript_HocBindingPatternAcceptsMultiLineTaggedTemplateContinuation()
    {
        // Same multi-line continuation support on the TypeScript side. The
        // TS HOC row uses the same `styled[.(\`]` branch as JavaScript (no
        // generic suffix on `styled` itself), so the shapes that need to be
        // pinned mirror the JavaScript test. TypeScript-specific binding
        // forms such as an optional `: ComponentType<Props>` type annotation
        // between the name and `=` are also exercised so the gate does not
        // get confused by an identifier that carries a type annotation.
        // Closes #240 follow-up (codex review #9 blocker, upstream issue #901).
        // TypeScript 側でも同じ複数行継続対応が必要。TS の HOC 行は `styled`
        // 自体に generic 接尾辞を載せない `styled[.(\`]` 分岐を JS と共有するため、
        // pin すべき形も JS テストとミラーする。加えて TS 特有の型注釈付き束縛
        // （`const Foo: ComponentType<Props> = styled.div\n\`...\``）も通過
        // させ、ゲートが型注釈付き識別子で混乱しないことを確認する。
        // Closes #240 follow-up（codex レビュー #9 の blocker 対応、上流 issue #901）。
        var content = """
            const MultiLineMember = styled.div
              `color: red;`;
            const MultiLineCall = styled(Component)
              `color: blue;`;
            const MultiLineAnnotated: ComponentType<Props> = styled.div
              `color: green;`;
            const AsiRejectedMember = styled.div
            const AsiFollower: number = 5;
            """;

        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MultiLineMember");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MultiLineCall");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MultiLineAnnotated");
        Assert.DoesNotContain(symbols, s => s.Name == "AsiRejectedMember");
    }

    [Fact]
    public void Extract_JavaScript_HocBindingPatternRejectsExpressionStatementWithBacktickOnNextStatement()
    {
        // ASI inserts a `;` between `styled.div` and the next line's expression
        // statement when that statement begins with an identifier / `await` /
        // other non-continuation token. The gate must inspect the first
        // meaningful character of each continuation line: only a backtick
        // (template itself) or `.` (member chain) may continue the expression.
        // `<` is intentionally not whitelisted because `<Foo>...` at statement
        // start is a JSX element (or TS cast), not a tagged-template generic
        // continuation. Closes #240 follow-up (codex review #10 and #11
        // blockers).
        // ASI は `styled.div` の次行が識別子始まり・`await` 始まり等の非継続トークンで
        // 始まる式文のとき、暗黙の `;` を挿入する。ゲートは継続行の最初の実トークンを
        // 見て判定する必要がある — バッククォート（テンプレート自体）か `.`（メンバー
        // チェーン）のみが式を継続可能で、それ以外は新しい文として走査を打ち切る。
        // `<` は JSX 要素 / TS キャストの開始にもなるため意図的に許可しない。
        // Closes #240 follow-up（codex レビュー #10 と #11 の blocker 対応）。
        var content = """
            const IdentifierStartFactory = styled.div
            foo(`not a template`)
            const AwaitStartFactory = styled.div
            await foo(`not a template`)
            const CallExprFactory = styled(Component)
            foo(`not a template`)
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "IdentifierStartFactory");
        Assert.DoesNotContain(symbols, s => s.Name == "AwaitStartFactory");
        Assert.DoesNotContain(symbols, s => s.Name == "CallExprFactory");
    }

    [Fact]
    public void Extract_TypeScript_HocBindingPatternRejectsExpressionStatementWithBacktickOnNextStatement()
    {
        // Same ASI-aware continuation rule on the TypeScript side. Closes #240
        // follow-up (codex review #10 blocker, upstream issue #910).
        // TypeScript 側でも同じ ASI 対応の継続ルールが必要。Closes #240 follow-up
        // （codex レビュー #10 の blocker 対応、上流 issue #910）。
        var content = """
            const IdentifierStartFactory = styled.div
            foo(`not a template`)
            const AwaitStartFactory = styled.div
            await foo(`not a template`)
            const CallExprFactory = styled(Component)
            foo(`not a template`)
            const AnnotatedIdentifierStart: StyledComponent<'div'> = styled.div
            foo(`not a template`)
            """;

        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "IdentifierStartFactory");
        Assert.DoesNotContain(symbols, s => s.Name == "AwaitStartFactory");
        Assert.DoesNotContain(symbols, s => s.Name == "CallExprFactory");
        Assert.DoesNotContain(symbols, s => s.Name == "AnnotatedIdentifierStart");
    }

    [Fact]
    public void Extract_JavaScript_HocBindingPatternRejectsJsxElementOnNextStatement()
    {
        // A `<Foo>...` continuation is a JSX element (standalone expression
        // statement) — NOT a tagged-template generic — so the styled-factory
        // candidate on the previous line must be rejected even though the
        // JSX element contains a backtick-delimited child. Closes #240
        // follow-up (codex review #11 blocker).
        // 継続行の先頭が `<Foo>...` の場合は JSX 要素（独立した式文）であり
        // tagged-template の generic 継続ではない。JSX 要素の子がバッククォートを
        // 含んでも、前行の styled factory 候補は不採用にする必要がある。
        // Closes #240 follow-up（codex レビュー #11 の blocker 対応）。
        var content = """
            const JsxElementFactory = styled.div
            <Foo>{`not a template`}</Foo>
            const JsxFragmentFactory = styled(Component)
            <><span>{`also not`}</span></>
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "JsxElementFactory");
        Assert.DoesNotContain(symbols, s => s.Name == "JsxFragmentFactory");
    }

    [Fact]
    public void Extract_TypeScript_HocBindingPatternRejectsJsxElementOnNextStatement()
    {
        // Same JSX-on-next-statement rejection on the TypeScript/TSX side —
        // `<Foo>...` can also be a TS type cast, and in either reading it is
        // still a new statement inserted by ASI, not a continuation of the
        // preceding styled expression. Closes #240 follow-up (codex review
        // #11 blocker).
        // TypeScript/TSX 側でも同様に `<Foo>...` は JSX 要素か TS キャストであり、
        // どちらの解釈でも ASI で挿入された新しい文であって先行式の継続ではない。
        // Closes #240 follow-up（codex レビュー #11 の blocker 対応）。
        var content = """
            const JsxElementFactory = styled.div
            <Foo>{`not a template`}</Foo>
            const AnnotatedJsxFactory: StyledComponent<'div'> = styled.div
            <Bar>{`also not`}</Bar>
            """;

        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "JsxElementFactory");
        Assert.DoesNotContain(symbols, s => s.Name == "AnnotatedJsxFactory");
    }

    [Fact]
    public void Extract_JavaScript_HocBindingPatternRejectsOperatorBetweenStyledAndTemplate()
    {
        // `styled.div + \`...\`` and `styled(Component) + \`...\`` are NOT
        // tagged-template bindings — the `+` operator at depth 0 between the
        // styled expression and the backtick breaks the tag-head continuation
        // chain. Closes #240 follow-up (codex review #12 blocker). Without the
        // depth-0 operator reject, the gate would happily walk past `+` to
        // the first backtick and accept the candidate as a phantom
        // `function NotStyled` symbol.
        // `styled.div + \`...\`` や `styled(Component) + \`...\`` は tagged-template
        // 束縛ではない — depth 0 の `+` 演算子が styled 式とバッククォートの間に
        // 入ることで tag-head 継続チェーンが切れる。Closes #240 follow-up
        // （codex レビュー #12 の blocker 対応）。depth-0 演算子除外がないと、ゲートが
        // `+` を跨いで最初のバッククォートに到達し phantom `function NotStyled` を
        // 出してしまう。
        var content = """
            const NotStyledMember = styled.div + `not a styled template`;
            const NotStyledCall = styled(Component) + `also not`;
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "NotStyledMember");
        Assert.DoesNotContain(symbols, s => s.Name == "NotStyledCall");
    }

    [Fact]
    public void Extract_TypeScript_HocBindingPatternRejectsOperatorBetweenStyledAndTemplate()
    {
        // TypeScript counterpart for the depth-0 operator reject — including
        // a typed-annotation variant — must still drop these phantom bindings.
        // Closes #240 follow-up (codex review #12 blocker).
        // TypeScript 側の depth-0 演算子除外（型注釈付きの変種を含む）。
        // Closes #240 follow-up（codex レビュー #12 の blocker 対応）。
        var content = """
            const NotStyledMember = styled.div + `not a styled template`;
            const NotStyledCall = styled(Component) + `also not`;
            const AnnotatedNotStyled: StyledComponent<'div'> = styled.div + `still not`;
            """;

        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "NotStyledMember");
        Assert.DoesNotContain(symbols, s => s.Name == "NotStyledCall");
        Assert.DoesNotContain(symbols, s => s.Name == "AnnotatedNotStyled");
    }

    [Fact]
    public void Extract_JavaScript_HocBindingPatternRejectsOperatorAfterStyledTemplate()
    {
        // `styled.div\`color: red\` + theme` is theme composition, not a styled
        // binding — even though the tag-head backtick is present, the depth-0
        // `+` operator after the closing backtick indicates the right-hand side
        // is a binary expression. The gate must walk past the template body and
        // still reject on the post-template operator. Closes #240 follow-up
        // (codex review #13 High blocker).
        // `styled.div\`color: red\` + theme` はテーマ合成式であって styled 束縛では
        // ない — tag head の backtick が存在しても、closing backtick 後の depth 0
        // `+` 演算子により右辺が二項式になっている。ゲートはテンプレート本体を
        // 読み飛ばした後でも post-template operator を検出して除外する必要がある。
        // Closes #240 follow-up（codex レビュー #13 High blocker）。
        var content = """
            const NotStyledPlusTheme = styled.div`color: red` + theme;
            const NotStyledCallPlusTheme = styled(Component)`color: blue` + theme;
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "NotStyledPlusTheme");
        Assert.DoesNotContain(symbols, s => s.Name == "NotStyledCallPlusTheme");
    }

    [Fact]
    public void Extract_TypeScript_HocBindingPatternRejectsOperatorAfterStyledTemplate()
    {
        // TypeScript counterpart for the post-template operator reject —
        // including a typed-annotation variant. Closes #240 follow-up
        // (codex review #13 High blocker).
        // TypeScript 側の post-template 演算子除外（型注釈付き変種を含む）。
        // Closes #240 follow-up（codex レビュー #13 High blocker）。
        var content = """
            const NotStyledPlusTheme = styled.div`color: red` + theme;
            const NotStyledCallPlusTheme = styled(Component)`color: blue` + theme;
            const AnnotatedNotStyledPlusTheme: StyledComponent<'div'> = styled.div`color: red` + theme;
            """;

        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Name == "NotStyledPlusTheme");
        Assert.DoesNotContain(symbols, s => s.Name == "NotStyledCallPlusTheme");
        Assert.DoesNotContain(symbols, s => s.Name == "AnnotatedNotStyledPlusTheme");
    }

    [Fact]
    public void Extract_TypeScript_HocBindingPatternAcceptsLongAttrsChainBeforeTemplate()
    {
        // Prettier-formatted `.attrs((props) => ({ ... }))` argument objects can
        // span more than ten lines before the backtick is reached. The gate's
        // lookahead window must be large enough so the trailing tagged template
        // is still recognized and the styled binding is kept. Closes #240
        // follow-up (codex review #13 Medium blocker).
        // Prettier 整形の `.attrs((props) => ({ ... }))` 引数オブジェクトは
        // バッククォート到達まで 10 行を超えることがある。lookahead window が
        // 十分広くないと末尾の tagged template を見落とし styled 束縛が落ちる。
        // Closes #240 follow-up（codex レビュー #13 Medium blocker）。
        var content = """
            const Tall = styled.div.attrs((props) => ({
              field1: props.value1,
              field2: props.value2,
              field3: props.value3,
              field4: props.value4,
              field5: props.value5,
              field6: props.value6,
              field7: props.value7,
              field8: props.value8,
              field9: props.value9,
              field10: props.value10,
            }))`
              color: red;
              padding: 8px;
            `;
            """;

        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Tall");
    }

    [Fact]
    public void Extract_TypeScript_HocBindingPatternAcceptsCallbackPropInsideFunctionTypeGeneric()
    {
        // Inline function-type generic arguments whose parameter object literal
        // carries a callback-prop with ITS OWN paren group
        // (`React.memo<(props: { onClick: (x: number) => void }) => JSX.Element>(Box)`)
        // must still be accepted. The shared TypeScriptOptionalHocTypeArgsPattern
        // now balances one level of nested parens inside each generic-argument paren
        // segment so real React callback-prop shapes do not get dropped. Closes #240.
        // 関数型 generic 引数の中にある引数オブジェクトに、さらに自前の paren を持つ
        // callback-prop が入る形
        // （`React.memo<(props: { onClick: (x: number) => void }) => JSX.Element>(Box)`）
        // もマッチさせる必要がある。共有定数 TypeScriptOptionalHocTypeArgsPattern は、
        // 各 generic 引数内の paren セグメントで 1 段のネスト paren を balance するように
        // なったため、実在する React の callback-prop 形を取りこぼさない。Closes #240.
        var content = """
            import React from 'react';

            const NestedCallbackPropMemo = React.memo<(props: { onClick: (x: number) => void }) => JSX.Element>(Box);
            const BareCallbackPropMemo = memo<(props: { onChange: (value: string) => void }) => JSX.Element>(Box);
            """;

        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "NestedCallbackPropMemo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "BareCallbackPropMemo");
    }

    [Fact]
    public void Extract_JavaScript_DetectsReExportSurfaceSymbols()
    {
        var content = """
            export * from './util';
            export { foo, bar } from './other'; // trailing comment
            export { default as Helper } from './helper';
            export * /* from './bogus-star' */ as ns from './ns';
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./util");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./other");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./helper");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./ns");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "foo");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "bar");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Helper");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "ns");
    }

    [Fact]
    public void Extract_JavaScript_DetectsMultilineStarReExportSurfaceSymbols()
    {
        var content = """
            export *
            from './util';
            export * /* from './bogus-star' */ as ns
            from './ns';
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./util");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./ns");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "ns");
    }

    [Fact]
    public void Extract_JavaScript_DetectsMinifiedReExportSurfaceSymbols()
    {
        var content = """
            export{foo as bar}from './other';
            export*as ns from './ns';
            export*from './util';
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./other");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./ns");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./util");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "bar");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "ns");
    }

    [Fact]
    public void Extract_JavaScript_DetectsNamedReExportWhenExportAndSpecifierListAreSplitAcrossLines()
    {
        var content = """
            export
            { foo, bar } from './other';
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./other");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "foo");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "bar");
    }

    [Fact]
    public void Extract_TypeScript_DetectsNamedAndTypeReExportSurfaceSymbols()
    {
        var content = """
            export {
              foo, // from './bogus'
              bar,
            } from './other';
            export { default as Helper } from './helper'; // trailing comment
            export type {
              User,
              Admin,
            } from './types';
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./other");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./helper");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./types");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "foo");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "bar");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Helper");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Admin");
    }

    [Fact]
    public void Extract_TypeScript_DetectsTypeOnlyStarReExportSurfaceSymbols()
    {
        var content = """
            export type * from './types';
            export type * as ns from './types-ns';
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./types");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./types-ns");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "ns");
    }

    [Fact]
    public void Extract_TypeScript_DetectsReExportSurfaceSymbolsWithImportAttributes()
    {
        var content = """
            export * from './util' with { type: 'json' };
            export { foo as bar } from './other' with { type: 'json' };
            export * from './legacy' assert { type: 'json' };
            export { baz as qux } from './older' assert { type: 'json' };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./util");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./other");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./legacy");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./older");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "bar");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "qux");
    }

    [Fact]
    public void Extract_TypeScript_DetectsMultilineStarReExportSurfaceSymbolsWithImportAttributes()
    {
        var content = """
            export * from './util' with {
              type: 'json'
            };
            export * as ns from './other' assert {
              type: 'json'
            };
            export type * from './types' with {
              type: 'json'
            };
            export type * as typeNs from './types-ns' assert {
              type: 'json'
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./util");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./other");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./types");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./types-ns");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "ns");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "typeNs");
    }

    [Fact]
    public void Extract_TypeScript_DetectsNamedReExportSurfaceSymbolsWhenImportAttributeBraceStartsOnNextLine()
    {
        var content = """
            export { foo as bar } from './other' with
            {
              type: 'json'
            };
            export type { User } from './types' assert
            {
              type: 'json'
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./other");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "./types");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "bar");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "User");
    }

    [Fact]
    public void Extract_JavaScript_StringBraceDoesNotBreakFollowingContainerAssignment()
    {
        var content = """"
            export class Example {
              foo() {
                const value = "}";
                return value;
              }

              bar() {
                return 1;
              }
            }
            """";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        var example = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Example"));
        var foo = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "foo"));
        var bar = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "bar"));

        Assert.Equal(10, example.EndLine);
        Assert.Equal(5, foo.EndLine);
        Assert.Equal("class", bar.ContainerKind);
        Assert.Equal("Example", bar.ContainerName);
    }

    [Fact]
    public void Extract_JavaScript_TemplateLiteralBraceDoesNotBreakFollowingContainerAssignment()
    {
        var content = """"
            export class Example {
              foo() {
                const value = `}`;
                return value;
              }

              bar() {
                return 1;
              }
            }
            """";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        var example = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Example"));
        var foo = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "foo"));
        var bar = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "bar"));

        Assert.Equal(10, example.EndLine);
        Assert.Equal(5, foo.EndLine);
        Assert.Equal("class", bar.ContainerKind);
        Assert.Equal("Example", bar.ContainerName);
    }

    [Fact]
    public void Extract_TypeScript_TemplateInterpolationBracesStillCountTowardMethodRange()
    {
        var content = """"
            export class Example {
              foo() {
                const value = `${format({ answer: 42 })}`;
                return value;
              }

              bar() {
                return 1;
              }
            }
            """";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var example = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Example"));
        var foo = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "foo"));
        var bar = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "bar"));

        Assert.Equal(10, example.EndLine);
        Assert.Equal(5, foo.EndLine);
        Assert.Equal("class", bar.ContainerKind);
        Assert.Equal("Example", bar.ContainerName);
    }

    [Fact]
    public void Extract_JavaScript_DetectsExportDefaultClassMembers()
    {
        var content = """""
            export default class DefaultJs {
                run() {}
            }
            """"";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "DefaultJs");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotInventExtendsAsAnonymousDefaultClassName()
    {
        var content = """
            export default class extends Base {
                run() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "extends");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotInventExtendsAsAnonymousDefaultDerivedClassName()
    {
        var content = """
            export default class extends mixin(Base) {
                run() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "extends");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_JavaScript_DetectsClassExpressionMethods()
    {
        var content = """
            const Service = class NamedService {
                run() {}
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "NamedService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run");
        var run = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "run"));
        Assert.Equal("class", run.ContainerKind);
        Assert.Equal("Service", run.ContainerName);
    }

    [Fact]
    public void Extract_JavaScript_DetectsMultilineExportedClassExpressionMethods()
    {
        var content = """
            export const Service =
                class {
                    run() {}
                };
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "Service");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service" && s.Signature == "export const Service = class {");
    }

    [Fact]
    public void Extract_JavaScript_DetectsParenthesizedClassExpressionMethods()
    {
        var content = "const Service = (class { run() {} });";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "Service");
    }

    [Fact]
    public void Extract_JavaScript_DetectsInlineClassMethods()
    {
        var content = "export class Inline { run() {} }";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Inline");
        var run = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "run"));
        Assert.Equal("class", run.ContainerKind);
        Assert.Equal("Inline", run.ContainerName);
    }

    [Fact]
    public void Extract_JavaScript_DetectsInlineMultipleMethods()
    {
        var content = "class Inline { first() {} second() {} }";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Inline");
        var first = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "first"));
        var second = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "second"));
        Assert.Equal("class", first.ContainerKind);
        Assert.Equal("Inline", first.ContainerName);
        Assert.Equal("first() {}", first.Signature);
        Assert.Equal("class", second.ContainerKind);
        Assert.Equal("Inline", second.ContainerName);
        Assert.Equal("second() {}", second.Signature);
    }

    [Fact]
    public void Extract_JavaScript_DetectsSameLineSiblingClassesWithDistinctMethodNames()
    {
        var content = "class A { first() {} } class B { second() {} }";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "A");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "B");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first" && s.ContainerName == "A");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second" && s.ContainerName == "B");
    }

    [Fact]
    public void Extract_JavaScript_DetectsSameLineSiblingClassesWithIdenticalMethodNames()
    {
        var content = "class A { run() {} } class B { run() {} }";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "A");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "B");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "A");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "B");
    }

    [Fact]
    public void Extract_JavaScript_DetectsSameLinePublicClassAfterStatementPrefix()
    {
        var content = "foo(); class Visible { keep() {} }";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Visible");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "Visible");
    }

    [Fact]
    public void Extract_JavaScript_DetectsSameLinePublicClassAfterFunctionLocalHiddenClass()
    {
        var content = "function outer(){ class Hidden { run() {} } } class Visible { keep() {} }";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "outer");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Visible");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "Visible");
    }

    [Fact]
    public void Extract_JavaScript_DetectsSameLineClassExpressionAfterStatementPrefix()
    {
        var content = "foo(); const Service = class Visible { keep() {} };";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Visible");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "Service");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service" && s.Signature == "const Service = class Visible { keep() {} }");
    }

    [Fact]
    public void Extract_JavaScript_DetectsStatementPrefixedDefaultExportClassSignatureFromExport()
    {
        var content = "const before = 1; export default (class { run() {} })";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default" && s.Signature == "export default (class { run() {} }");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_JavaScript_DetectsInlineDefaultExportClassMethods()
    {
        var content = "export default class Inline { run() {} }";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Inline");
        var run = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "run"));
        Assert.Equal("class", run.ContainerKind);
        Assert.Equal("Inline", run.ContainerName);
    }

    [Fact]
    public void Extract_JavaScript_DetectsInlineDefaultExportMultipleMethods()
    {
        var content = "export default class { first() {} second() {} }";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first" && s.ContainerName == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_JavaScript_DetectsParenthesizedDefaultExportClassMembers()
    {
        var content = """
            export default (class {
                run() {}
            });
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_JavaScript_DetectsMultilineParenthesizedDefaultExportClassSignature()
    {
        var content = """
            export default
            (
                class {
                    run() {}
                }
            );
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default" && s.Signature == "export default ( class {");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_JavaScript_DetectsInlineModifierNamedMethods()
    {
        var content = "export default class { async() {} static() {} keep() {} }";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "async" && s.ContainerName == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "static" && s.ContainerName == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "async" && s.Signature == "async() {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "static" && s.Signature == "static() {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.Signature == "keep() {}");
    }

    [Fact]
    public void Extract_JavaScript_DetectsInlineMethodsWithDefaultArguments()
    {
        var content = "class Example { method(x = 1) {} visible() {} }";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "method" && s.ContainerName == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "visible" && s.ContainerName == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "method" && s.Signature == "method(x = 1) {}");
    }

    [Fact]
    public void Extract_JavaScript_DetectsInlinePrivateAndGeneratorMethods()
    {
        var content = "class Example { #hidden() {} *iterator() {} async *stream() {} visible() {} }";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "#hidden" && s.ContainerName == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "iterator" && s.ContainerName == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "stream" && s.ContainerName == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "visible" && s.ContainerName == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "#hidden" && s.Signature == "#hidden() {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "iterator" && s.Signature == "*iterator() {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "stream" && s.Signature == "async *stream() {}");
    }

    [Fact]
    public void Extract_JavaScript_DetectsInlineComputedMethods()
    {
        var content = "class Example { ['computed']() {} [Symbol.iterator]() {} visible() {} }";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "['computed']" && s.ContainerName == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "[Symbol.iterator]" && s.ContainerName == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "visible" && s.ContainerName == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "['computed']" && s.Signature == "['computed']() {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "[Symbol.iterator]" && s.Signature == "[Symbol.iterator]() {}");
    }

    [Fact]
    public void Extract_JavaScript_PreservesModifierMetadataForComputedMethods()
    {
        var content = """
            class Example {
                async [Symbol.asyncIterator]() {}
                get [Symbol.toStringTag]() {}
                set [key](value) {}
                async *[Symbol.iterator]() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "[Symbol.asyncIterator]" && s.Signature == "async [Symbol.asyncIterator]() {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "[Symbol.toStringTag]" && s.Signature == "get [Symbol.toStringTag]() {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "[key]" && s.Signature == "set [key](value) {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "[Symbol.iterator]" && s.Signature == "async *[Symbol.iterator]() {}");
    }

    [Fact]
    public void Extract_JavaScript_DetectsQuotedAndNumericLiteralMethodNames()
    {
        var content = """
            class Example {
                "run"() {}
                'stop'() {}
                1() {}
                1.5() {}
                0x10() {}
                1_000() {}
                next() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "\"run\"" && s.Signature == "\"run\"() {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "'stop'" && s.Signature == "'stop'() {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "1" && s.Signature == "1() {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "1.5" && s.Signature == "1.5() {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "0x10" && s.Signature == "0x10() {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "1_000" && s.Signature == "1_000() {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "next" && s.Signature == "next() {}");
    }

    [Fact]
    public void Extract_JavaScript_DetectsEscapedQuotedLiteralMethodNames()
    {
        var content = """
            class Example {
                "a\"b"() {}
                'c\'d'() {}
                next() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "\"a\\\"b\"" && s.Signature == "\"a\\\"b\"() {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "'c\\'d'" && s.Signature == "'c\\'d'() {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "next" && s.Signature == "next() {}");
    }

    [Theory]
    [InlineData(
        """
        class Example {
            handler = function namedHandler() {};
            keep() {}
        }
        """,
        "namedHandler")]
    [InlineData(
        """
        class Example {
            handler = function () {};
            keep() {}
        }
        """,
        "function")]
    [InlineData(
        """
        class Example {
            field = { inner() {} };
            keep() {}
        }
        """,
        "inner")]
    [InlineData(
        """
        class Example {
            field = class Inner { run() {} };
            keep() {}
        }
        """,
        "run")]
    public void Extract_JavaScript_DoesNotTreatClassFieldInitializerMembersAsClassMethods(string content, string unexpectedMethod)
    {
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "Example");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == unexpectedMethod && s.ContainerName == "Example");
    }

    [Fact]
    public void Extract_JavaScript_SemicolonlessFieldInitializerDoesNotHideComputedOrGeneratorMethods()
    {
        var content = """
            class Example {
                field = foo
                [Symbol.iterator]() {}
                *generate() {}
                next() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "[Symbol.iterator]" && s.ContainerName == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "generate" && s.ContainerName == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "next" && s.ContainerName == "Example");
    }

    [Fact]
    public void Extract_JavaScript_DetectsInlineClassExpressionMethods()
    {
        var content = "const Service = class NamedService { run() {} };";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        var run = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "run"));
        Assert.Equal("class", run.ContainerKind);
        Assert.Equal("Service", run.ContainerName);
    }

    [Fact]
    public void Extract_JavaScript_DetectsCommonJsExportsClassExpressionMethods()
    {
        var content = "exports.Service = class NamedService { run() {} };";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "NamedService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "Service");
    }

    [Fact]
    public void Extract_JavaScript_DetectsDollarPrefixedClassExpressionBindings()
    {
        var content = """
            export const $Service = class {
                run() {}
            };

            exports.$Handler = class {
                keep() {}
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "$Service");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "$Service");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "$Handler");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "$Handler");
    }

    [Fact]
    public void Extract_JavaScript_DetectsCommonJsModuleExportsClassExpressionMethods()
    {
        var content = "module.exports = class { run() {} };";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_JavaScript_DetectsMultilineCommonJsModuleExportsClassExpressionMethods()
    {
        var content = """
            module.exports =
                class {
                    run() {}
                };
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_JavaScript_DetectsParenthesizedCommonJsModuleExportsClassExpressionMethods()
    {
        var content = """
            module.exports = (class {
                run() {}
            });
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_JavaScript_DetectsCommonJsModuleExportsPropertyClassExpressionMethods()
    {
        var content = "module.exports.Service = class { run() {} };";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "Service");
    }

    [Fact]
    public void Extract_JavaScript_DetectsCommonJsClassExpressionInsideTopLevelConditionalBlock()
    {
        var content = """
            if (typeof module !== "undefined") {
                module.exports = class {
                    run() {}
                };
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotLeakBlockScopedClassesInsideConditionalBlocks()
    {
        var content = """
            if (flag) {
                const Hidden = class {
                    run() {}
                };

                class LocalDecl {
                    keep() {}
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "LocalDecl");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "keep");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotLeakClassMethodLocalClassExpressionMethods()
    {
        var content = """
            export class Outer {
                method() {
                    const Inner = class {
                        run() {}
                    };
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Outer");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "method" && s.ContainerName == "Outer");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Inner");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotLeakClassMethodDirectLocalClasses()
    {
        var content = """
            class Outer {
                method() {
                    class Hidden {
                        run() {}
                    }
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Outer");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "method" && s.ContainerName == "Outer");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotLeakFunctionLocalClassExpressionMethods()
    {
        var content = """
            function outer() {
                const Service = class {
                    run() {}
                };
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "outer");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotLeakDirectFunctionLocalClasses()
    {
        var content = """
            function outer() {
                class Hidden {
                    run() {}
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "outer");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotLeakCommonJsFunctionExpressionLocalClassMethods()
    {
        var content = """
            exports.handler = function () {
                const Local = class {
                    inside() {}
                };
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Local");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "inside");
    }

    [Fact]
    public void Extract_JavaScript_DetectsCommonJsNamedExportAssignments()
    {
        var content = """
            module.exports.foo = function foo() { return 1; };
            module.exports.bar = () => 2;
            exports.baz = 42;
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "foo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "bar");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "baz");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotTreatCommonJsNamedExportComparisonsAsAssignments()
    {
        var content = """
            module.exports.foo === undefined;
            exports.bar == null;
            module.exports.baz !== 1;
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Empty(symbols);
    }

    [Fact]
    public void Extract_JavaScript_DetectsMultilineCommonJsNamedExportAssignments()
    {
        var content = """
            module.exports.foo =
              async () => {};
            module.exports.bar =
              () => 2;
            exports.baz =
              42;
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "foo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "bar");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "baz");
    }

    [Fact]
    public void Extract_JavaScript_DetectsParenthesizedMultilineCommonJsNamedExportAssignments()
    {
        var content = """
            module.exports.foo =
              (
                async () => 1
              );
            module.exports.bar =
              (
                function () {
                  return 2;
                }
              );
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "foo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "bar");
    }

    [Fact]
    public void Extract_JavaScript_DetectsParenthesizedSameLineCommonJsNamedExportAssignments()
    {
        var content = """
            module.exports.foo = (function () { return 1; });
            module.exports.bar = (async function () { return 2; });
            module.exports.baz = (() => 3);
            exports.qux = (42);
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "foo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "bar");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "baz");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "qux");
    }

    [Fact]
    public void Extract_JavaScript_CommonJsNamedExportFunctionsPreserveMultilineBraceBodyRanges()
    {
        var content = """
            module.exports.foo = function ()
            {
              return 1;
            };
            module.exports.bar = () =>
            {
              return 2;
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        var foo = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "foo"));
        Assert.Equal(1, foo.StartLine);
        Assert.Equal(4, foo.EndLine);
        Assert.Equal(2, foo.BodyStartLine);
        Assert.Equal(4, foo.BodyEndLine);

        var bar = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "bar"));
        Assert.Equal(5, bar.StartLine);
        Assert.Equal(8, bar.EndLine);
        Assert.Equal(6, bar.BodyStartLine);
        Assert.Equal(8, bar.BodyEndLine);
    }

    [Fact]
    public void Extract_JavaScript_DetectsConditionalCommonJsNamedExportAssignmentsInTopLevelBlocks()
    {
        var content = """
            if (process.env.FEATURE) {
              module.exports.enabled = function () {
                return true;
              };
            }
            if (process.env.FLAG) {
              exports.flag = 1;
            }
            function setup() {
              module.exports.hidden = function () {
                return false;
              };
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "enabled");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "flag");
        Assert.DoesNotContain(symbols, s => s.Name == "hidden");
    }

    [Fact]
    public void Extract_TypeScript_GenericArrowCommonJsNamedExportFunctionsPreserveMultilineBraceBodyRanges()
    {
        var content = """
            module.exports.foo = <T>(value: T) =>
            {
              return value;
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var foo = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "foo"));
        Assert.Equal(1, foo.StartLine);
        Assert.Equal(4, foo.EndLine);
        Assert.Equal(2, foo.BodyStartLine);
        Assert.Equal(4, foo.BodyEndLine);
    }

    [Fact]
    public void Extract_TypeScript_DetectsGenericArrowCommonJsNamedExportAssignments()
    {
        var content = """
            module.exports.foo = <T>(value: T) => value;
            module.exports.bar =
              <T>(value: T) => value;
            module.exports.baz = (<T>(value: T) => value);
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "foo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "bar");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "baz");
    }

    [Fact]
    public void Extract_TypeScript_DetectsMultilineAndConstrainedGenericArrowCommonJsNamedExportAssignments()
    {
        var content = """
            module.exports.foo = <T>(
              value: T
            ) => value;
            module.exports.bar = <T extends (...args: any[]) => number>(value: T) => value;
            module.exports.baz = async <T>(
              value: T
            ) => value;
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "foo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "bar");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "baz");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotTreatCommonJsNamedExportComparisonsAsAssignments()
    {
        var content = """
            module.exports.foo === undefined;
            exports.bar == null;
            module.exports.baz !== 1;
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Empty(symbols);
    }

    [Fact]
    public void Extract_JavaScript_DoesNotTreatCommonJsNamedExportIdentifierPrefixesAsFunctionsOrClasses()
    {
        var content = """
            module.exports.foo = functionCall();
            module.exports.bar = classyThing;
            module.exports.baz = (functionCall());
            exports.qux = (classyThing);
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "foo");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "bar");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "baz");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "qux");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && (s.Name == "foo" || s.Name == "baz"));
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && (s.Name == "bar" || s.Name == "qux"));
    }

    [Fact]
    public void Extract_JavaScript_DetectsExportedObjectLiteralAliasProperties()
    {
        var content = """
            const foo = 1;
            function inner() { return 3; }
            function named() { return 4; }
            const answer = 42;
            module.exports = { foo, alias: inner, named, method() {} };
            export default { answer };
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "foo" && s.ContainerKind == "object" && s.ContainerName == "module.exports");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "alias" && s.ContainerKind == "object" && s.ContainerName == "module.exports");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "named" && s.ContainerKind == "object" && s.ContainerName == "module.exports");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "method" && s.ContainerKind == "object" && s.ContainerName == "module.exports");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "answer" && s.ContainerKind == "object" && s.ContainerName == "default");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "inner" && s.ContainerKind == "object" && s.ContainerName == "module.exports");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotTreatQuotedOrComputedExportedObjectLiteralKeysAsValueSideShorthandProperties()
    {
        var content = """
            module.exports = { 'foo': bar, [baz]: qux, answer: 42 };
            export default { [name]: value, visible };
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "answer" && s.ContainerKind == "object" && s.ContainerName == "module.exports");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "visible" && s.ContainerKind == "object" && s.ContainerName == "default");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "bar" && s.ContainerKind == "object" && s.ContainerName == "module.exports");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "qux" && s.ContainerKind == "object" && s.ContainerName == "module.exports");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "value" && s.ContainerKind == "object" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotTreatExportedObjectLiteralSpreadsAsProperties()
    {
        var content = """
            const rest = source;
            const defaults = source;
            const answer = 42;
            module.exports = { ...rest, actual: 1, config: { ...rest } };
            export default { ...defaults, answer };
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "actual" && s.ContainerKind == "object" && s.ContainerName == "module.exports");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "config" && s.ContainerKind == "object" && s.ContainerName == "module.exports");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "answer" && s.ContainerKind == "object" && s.ContainerName == "default");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "rest" && s.ContainerKind == "object" && s.ContainerName == "module.exports");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "defaults" && s.ContainerKind == "object" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_TypeScript_DetectsExportedObjectLiteralShorthandProperties()
    {
        var content = """
            const foo = 1;
            const bar = 2;
            module.exports = {
              foo,
              bar,
              baz: foo,
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "foo" && s.ContainerKind == "object" && s.ContainerName == "module.exports");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "bar" && s.ContainerKind == "object" && s.ContainerName == "module.exports");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "baz" && s.ContainerKind == "object" && s.ContainerName == "module.exports");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotLeakBlocklessArrowReturnedClasses()
    {
        var content = """
            const factory = () =>
                class Hidden {
                    method() {}
                };
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "factory");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "method");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotLeakWrappedBlocklessArrowReturnedClassesAndKeepsRange()
    {
        var content = """
            const factory = () =>
                wrap(
                    class Hidden {
                        method() {}
                    }
                );
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        var factory = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "factory"));
        Assert.Equal(1, factory.StartLine);
        Assert.Equal(6, factory.EndLine);
        Assert.Equal(2, factory.BodyStartLine);
        Assert.Equal(6, factory.BodyEndLine);
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "method");
    }

    [Fact]
    public void Extract_JavaScript_BlocklessArrowWithoutSemicolonDoesNotConsumeFollowingTopLevelClass()
    {
        var content = """
            const factory = () =>
                class Hidden {
                    method() {}
                }
            class Visible {
                keep() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        var factory = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "factory"));
        Assert.Equal(1, factory.StartLine);
        Assert.Equal(4, factory.EndLine);
        Assert.Equal(2, factory.BodyStartLine);
        Assert.Equal(4, factory.BodyEndLine);
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Visible");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "Visible");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "method");
    }

    [Fact]
    public void Extract_JavaScript_BlocklessArrowWithoutSemicolonDoesNotConsumeFollowingExpressionStatement()
    {
        var content = """
            const factory = () =>
                class Hidden {
                    method() {}
                }
            foo();
            class Visible {
                keep() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        var factory = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "factory"));
        Assert.Equal(4, factory.EndLine);
        Assert.Equal(4, factory.BodyEndLine);
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Visible");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "Visible");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
    }

    [Fact]
    public void Extract_JavaScript_BlocklessArrowWithoutSemicolonDoesNotHideFollowingCommonJsClassExport()
    {
        var content = """
            const factory = () =>
                class Hidden {
                    method() {}
                }
            exports.Service = class Visible {
                keep() {}
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        var factory = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "factory"));
        Assert.Equal(4, factory.EndLine);
        Assert.Equal(4, factory.BodyEndLine);
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "Service");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotLeakIifeLocalClassMethods()
    {
        var content = """
            (function () {
                const Local = class {
                    inside() {}
                };
            })();
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Local");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "inside");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotLeakStaticBlockLocalClassMethods()
    {
        var content = """
            class Outer {
                static {
                    const Local = class {
                        inside() {}
                    };
                }

                keep() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Outer");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Local");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "inside");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotLeakDirectStaticBlockLocalClasses()
    {
        var content = """
            class Outer {
                static {
                    class Local {
                        inside() {}
                    }
                }

                keep() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Outer");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Local");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "inside");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotLeakObjectLiteralConciseMethodLocalClasses()
    {
        var content = """
            const obj = {
                method() {
                    class Inner {
                        run() {}
                    }
                }
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Inner");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotLeakGetterSetterLocalClasses()
    {
        var content = """
            const obj = {
                get value() {
                    class HiddenGetter {
                        run() {}
                    }
                    return 1;
                },
                set value(input) {
                    class HiddenSetter {
                        run() {}
                    }
                }
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "HiddenGetter");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "HiddenSetter");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_JavaScript_PreservesGetterSetterSignaturesAndMethodNamedGetSet()
    {
        var content = """
            class Example {
                get value() {}
                set value(input) {}
                get() {}
                set() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "value" && s.Signature == "get value() {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "value" && s.Signature == "set value(input) {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "get" && s.Signature == "get() {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "set" && s.Signature == "set() {}");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotLeakNamedClassExpressionsAfterColon()
    {
        var content = """
            const pick = cond ? value : class Hidden { method() {} };
            const obj = { field: class Inner { run() {} } };
            class Visible { ok() {} }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Inner");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "method");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Visible");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ok" && s.ContainerName == "Visible");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotTreatControlFlowBlocksAsFunctions()
    {
        var content = """
            class Parser {
                parse(value) {
                    if (value) {
                    }

                    for (const item of value.items) {
                    }

                    while (value.ready) {
                    }

                    switch (value.mode) {
                        case "fast":
                            break;
                    }
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Parser");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "parse");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "if");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "for");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "while");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "switch");
    }

    [Fact]
    public void Extract_JavaScript_AllowsKeywordNamedMethodsAtClassBodyDepthZero()
    {
        var content = """
            class KeywordMethods {
                if() {}
                catch() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "KeywordMethods");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "if");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "catch");
    }

    [Fact]
    public void Extract_JavaScript_IgnoresRegexLiteralBracesAndBlockCommentMethodShapes()
    {
        var content = """
            class Example {
                /*
                    fake() {
                    }
                */
                first() {
                    const open = /{/;
                    const close = /}/;
                }

                second() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "fake");
    }

    [Fact]
    public void Extract_JavaScript_KeepsSiblingMethodsAfterWrappedControlFlowRegexLiterals()
    {
        var content = """
            class Example {
                first(value) {
                    if (
                        ready
                    ) /{/.test(value);
                }

                second(value) {
                    if (first) {
                    }
                    else if (
                        secondReady
                    ) /{/.test(value);
                }

                third() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "third");
    }

    [Fact]
    public void Extract_JavaScript_IgnoresHeaderObjectLiteralBracesBeforeClassBody()
    {
        var content = """
            class Derived extends mixin({ value: true }) {
                run() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Derived");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotTreatHeaderComparisonAsGenericAngleDepth()
    {
        var content = """
            class Derived extends mixin(a < b ? Base : Fallback) {
                run() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Derived");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_JavaScript_KeepsSiblingMethodsAfterElseRegexLiteral()
    {
        var content = """
            class Example {
                first(value) {
                    if (cond) {
                    }
                    else /{/.test(value);
                }

                second() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second");
    }

    [Fact]
    public void Extract_JavaScript_KeepsSiblingMethodsAfterDoAndFinallyRegexLiterals()
    {
        var content = """
            class Example {
                first(value) {
                    do /{/.test(value); while (cond);
                }

                second(value) {
                    try {
                    }
                    finally /{/.test(value);
                }

                third() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "third");
    }

    [Fact]
    public void Extract_JavaScript_FunctionRangeIgnoresComparisonAngleBracketsInParameters()
    {
        var content = """
            function choose(value = a < b ? one : two) {
                return value;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        var choose = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "choose"));
        Assert.Equal(1, choose.StartLine);
        Assert.Equal(3, choose.EndLine);
        Assert.Equal(1, choose.BodyStartLine);
        Assert.Equal(3, choose.BodyEndLine);
    }

    [Fact]
    public void Extract_TypeScript_DetectsAbstractClassAndNamespace()
    {
        var content = "export abstract class BaseService {\n    abstract getName(): string;\n}\ndeclare module 'express' {\n    interface Request { }\n}\nnamespace App.Models {\n    export type ID = string;\n}";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "BaseService");
        // Quoted ambient module declaration / 引用符付きアンビエントモジュール宣言
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "express");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "App.Models");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "ID");
    }

    [Fact]
    public void Extract_TypeScript_DetectsDeclarationOnlyMembersInDeclareClassAndInterface()
    {
        var content = """
            declare class Service {
                run(): void;
                fetch<T>(id: string): Promise<T>;
            }

            interface Api {
                ping(): void;
                fetch<T>(id: string): Promise<T>;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerKind == "class" && s.ContainerName == "Service" && s.BodyStartLine == null);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "fetch" && s.ContainerKind == "class" && s.ContainerName == "Service" && s.BodyStartLine == null);
        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "Api");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ping" && s.ContainerKind == "interface" && s.ContainerName == "Api" && s.BodyStartLine == null);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "fetch" && s.ContainerKind == "interface" && s.ContainerName == "Api" && s.BodyStartLine == null);
    }

    [Fact]
    public void Extract_TypeScript_DoesNotMergeAbstractMemberIntoFollowingConcreteMethod()
    {
        var content = """
            export default abstract class Example {
                abstract run(): void;

                keep(): { value: string } {
                    return { value: "x" };
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run" && s.Signature != null && s.Signature.Contains("keep()", StringComparison.Ordinal));
        var keep = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "keep"));
        Assert.Equal("{ value: string }", keep.ReturnType);
        Assert.Equal("keep(): { value: string } {", keep.Signature);
    }

    [Fact]
    public void Extract_TypeScript_DetectsExportDefaultClassMembers()
    {
        var content = """
            export default class DefaultTs {
                run(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "DefaultTs");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_JavaScript_DetectsMultilineAnonymousDefaultExportClassMembers()
    {
        var content = """
            export default class
            extends Base
            {
                run() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotInventExtendsAsAnonymousDefaultClassName()
    {
        var content = """
            export default class extends Base {
                run(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "extends");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotInventExtendsAsAnonymousDefaultDerivedClassName()
    {
        var content = """
            export default class extends mixin(Base) {
                run(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "extends");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotInventImplementsAsAnonymousDefaultClassName()
    {
        var content = """
            export default class implements Runnable {
                run(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "implements");
        var run = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "run"));
        Assert.Equal("class", run.ContainerKind);
        Assert.Equal("default", run.ContainerName);
    }

    [Fact]
    public void Extract_TypeScript_DetectsClassExpressionMethods()
    {
        var content = """
            const Service = class NamedService {
                run(): void {}
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "NamedService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run");
        var run = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "run"));
        Assert.Equal("class", run.ContainerKind);
        Assert.Equal("Service", run.ContainerName);
    }

    [Fact]
    public void Extract_TypeScript_DetectsInlineClassMethods()
    {
        var content = "export class Inline { run(): void {} }";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Inline");
        var run = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "run"));
        Assert.Equal("class", run.ContainerKind);
        Assert.Equal("Inline", run.ContainerName);
    }

    [Fact]
    public void Extract_TypeScript_DetectsInlineMultipleMethods()
    {
        var content = "export class Inline { first(): void {} second(): void {} }";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Inline");
        var first = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "first"));
        var second = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "second"));
        Assert.Equal("class", first.ContainerKind);
        Assert.Equal("Inline", first.ContainerName);
        Assert.Equal("first(): void {}", first.Signature);
        Assert.Equal("class", second.ContainerKind);
        Assert.Equal("Inline", second.ContainerName);
        Assert.Equal("second(): void {}", second.Signature);
    }

    [Fact]
    public void Extract_TypeScript_DetectsSameLineSiblingClassesWithDistinctMethodNames()
    {
        var content = "export class A { first(): void {} } export class B { second(): void {} }";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "A");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "B");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first" && s.ContainerName == "A");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second" && s.ContainerName == "B");
    }

    [Fact]
    public void Extract_TypeScript_DetectsSameLineClassExpressionAfterStatementPrefixWithCleanSignature()
    {
        var content = "foo(); export const Service = class Visible { keep(): void {} };";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service" && s.Signature == "export const Service = class Visible { keep(): void {} }");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Visible");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "Service");
    }

    [Fact]
    public void Extract_TypeScript_DetectsSameLineSiblingClassesWithIdenticalMethodNames()
    {
        var content = "export class A { run(): void {} } export class B { run(): void {} }";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "A");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "B");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "A");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "B");
    }

    [Fact]
    public void Extract_TypeScript_DetectsSameLinePublicClassAfterStatementPrefix()
    {
        var content = "foo(); class Visible { keep(): void {} }";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Visible");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "Visible");
    }

    [Fact]
    public void Extract_TypeScript_DetectsSameLinePublicClassAfterFunctionLocalHiddenClass()
    {
        var content = "function outer(): void { class Hidden { run(): void {} } } class Visible { keep(): void {} }";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "outer");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Visible");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "Visible");
    }

    [Fact]
    public void Extract_TypeScript_DetectsSameLineClassExpressionAfterStatementPrefix()
    {
        var content = "foo(); const Service = class Visible { keep(): void {} };";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Visible");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "Service");
    }

    [Fact]
    public void Extract_TypeScript_DetectsInlineDefaultExportClassMethods()
    {
        var content = "export default class Inline { run(): void {} }";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Inline");
        var run = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "run"));
        Assert.Equal("class", run.ContainerKind);
        Assert.Equal("Inline", run.ContainerName);
    }

    [Fact]
    public void Extract_TypeScript_DetectsTypeAliasesAsImports()
    {
        var content = """
            export type Pair<T> = [T, T];
            type Callback = (x: number) => number;
            declare type User = { name: string; age: number };
            interface Admin { perms: string[]; }
            class Person { name: string = ""; }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Pair");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Callback");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "Admin");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Person");
    }

    [Fact]
    public void Extract_TypeScript_DetectsInlineDefaultExportMultipleMethods()
    {
        var content = "export default class { first(): void {} second(): void {} }";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first" && s.ContainerName == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_TypeScript_DetectsParenthesizedDefaultExportClassMembers()
    {
        var content = """
            export default (class {
                run(): void {}
            });
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_TypeScript_DetectsMultilineParenthesizedDefaultExportClassSignature()
    {
        var content = """
            export default
            (
                class {
                    run(): void {}
                }
            );
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default" && s.Signature == "export default ( class {");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_TypeScript_DetectsAnonymousAbstractDefaultExportClassMembers()
    {
        var content = """
            export default abstract class {
                run(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_TypeScript_DetectsMultilineAnonymousAbstractDefaultExportClassMembers()
    {
        var content = """
            export default abstract class
            extends Base
            {
                run(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_TypeScript_DetectsAnonymousGenericDefaultExportClassMembers()
    {
        var content = """
            export default class<T> extends Base<{ value: string }> {
                run(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_TypeScript_DetectsAnonymousAbstractGenericDefaultExportClassMembers()
    {
        var content = """
            export default abstract class<T> extends Base<{ value: string }> {
                run(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_TypeScript_DetectsInlineModifierNamedMethods()
    {
        var content = "export class Example { async(): void {} static(): void {} keep(): void {} }";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "async" && s.ContainerName == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "static" && s.ContainerName == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "async" && s.Signature == "async(): void {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "static" && s.Signature == "static(): void {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.Signature == "keep(): void {}");
    }

    [Fact]
    public void Extract_TypeScript_DetectsInlineMethodsWithDefaultArguments()
    {
        var content = "class Example { method(x: number = 1): void {} visible(): void {} }";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "method" && s.ContainerName == "Example" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "visible" && s.ContainerName == "Example" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "method" && s.Signature == "method(x: number = 1): void {}");
    }

    [Fact]
    public void Extract_TypeScript_DetectsInlinePrivateAndGeneratorMethods()
    {
        var content = "class Example { #hidden(): void {} *iterator(): Iterable<number> {} async *stream(): AsyncIterable<number> {} visible(): void {} }";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "#hidden" && s.ContainerName == "Example" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "iterator" && s.ContainerName == "Example" && s.ReturnType == "Iterable<number>");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "stream" && s.ContainerName == "Example" && s.ReturnType == "AsyncIterable<number>");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "visible" && s.ContainerName == "Example" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "#hidden" && s.Signature == "#hidden(): void {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "iterator" && s.Signature == "*iterator(): Iterable<number> {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "stream" && s.Signature == "async *stream(): AsyncIterable<number> {}");
    }

    [Fact]
    public void Extract_TypeScript_DetectsInlineComputedMethods()
    {
        var content = "class Example { ['computed'](): void {} [Symbol.iterator](): Iterable<number> {} visible(): void {} }";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "['computed']" && s.ContainerName == "Example" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "[Symbol.iterator]" && s.ContainerName == "Example" && s.ReturnType == "Iterable<number>");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "visible" && s.ContainerName == "Example" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "['computed']" && s.Signature == "['computed'](): void {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "[Symbol.iterator]" && s.Signature == "[Symbol.iterator](): Iterable<number> {}");
    }

    [Fact]
    public void Extract_TypeScript_PreservesModifierMetadataForComputedMethods()
    {
        var content = """
            class Example {
                async [Symbol.asyncIterator](): AsyncGenerator<number> {}
                public static [Symbol.iterator](): IterableIterator<number> {}
                get [Symbol.toStringTag](): string {}
                set [key](value: string) {}
                async *[Symbol.dispose](): AsyncGenerator<string> {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "[Symbol.asyncIterator]" && s.Signature == "async [Symbol.asyncIterator](): AsyncGenerator<number> {}" && s.ReturnType == "AsyncGenerator<number>");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "[Symbol.iterator]" && s.Signature == "public static [Symbol.iterator](): IterableIterator<number> {}" && s.Visibility == "public" && s.ReturnType == "IterableIterator<number>");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "[Symbol.toStringTag]" && s.Signature == "get [Symbol.toStringTag](): string {}" && s.ReturnType == "string");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "[key]" && s.Signature == "set [key](value: string) {}");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "[Symbol.dispose]" && s.Signature == "async *[Symbol.dispose](): AsyncGenerator<string> {}" && s.ReturnType == "AsyncGenerator<string>");
    }

    [Fact]
    public void Extract_TypeScript_DetectsStringAndTemplateLiteralReturnTypes()
    {
        var content = """
            export class Example {
                literal(): 'a' {}
                union(): 'a' | 'b' {}
                message(): `a${string}` {}
                next(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "literal" && s.Signature == "literal(): 'a' {}" && s.ReturnType == "'a'");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "union" && s.Signature == "union(): 'a' | 'b' {}" && s.ReturnType == "'a' | 'b'");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "message" && s.Signature == "message(): `a${string}` {}" && s.ReturnType == "`a${string}`");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "next" && s.Signature == "next(): void {}" && s.ReturnType == "void");
    }

    [Fact]
    public void Extract_TypeScript_DetectsEscapedStringLiteralReturnTypes()
    {
        var content = """
            export class Example {
                method(): "a\"b" {}
                keep(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "method" && s.Signature == "method(): \"a\\\"b\" {}" && s.ReturnType == "\"a\\\"b\"");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.Signature == "keep(): void {}" && s.ReturnType == "void");
    }

    [Fact]
    public void Extract_TypeScript_DetectsQuotedAndNumericLiteralMethodNames()
    {
        var content = """
            export class Example {
                "run"(): void {}
                'stop'(): void {}
                1(): void {}
                1.5(): void {}
                0x10(): void {}
                1_000(): void {}
                next(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "\"run\"" && s.Signature == "\"run\"(): void {}" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "'stop'" && s.Signature == "'stop'(): void {}" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "1" && s.Signature == "1(): void {}" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "1.5" && s.Signature == "1.5(): void {}" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "0x10" && s.Signature == "0x10(): void {}" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "1_000" && s.Signature == "1_000(): void {}" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "next" && s.Signature == "next(): void {}" && s.ReturnType == "void");
    }

    [Fact]
    public void Extract_TypeScript_DetectsEscapedQuotedLiteralMethodNames()
    {
        var content = """
            export class Example {
                "a\"b"(): void {}
                'c\'d'(): void {}
                next(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "\"a\\\"b\"" && s.Signature == "\"a\\\"b\"(): void {}" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "'c\\'d'" && s.Signature == "'c\\'d'(): void {}" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "next" && s.Signature == "next(): void {}" && s.ReturnType == "void");
    }

    [Theory]
    [InlineData(
        """
        export class Example {
            handler = function namedHandler(): void {};
            keep(): void {}
        }
        """,
        "namedHandler")]
    [InlineData(
        """
        export class Example {
            handler = function (): void {};
            keep(): void {}
        }
        """,
        "function")]
    [InlineData(
        """
        export class Example {
            field = { inner(): void {} };
            keep(): void {}
        }
        """,
        "inner")]
    [InlineData(
        """
        export class Example {
            field = class Inner { run(): void {} };
            keep(): void {}
        }
        """,
        "run")]
    public void Extract_TypeScript_DoesNotTreatClassFieldInitializerMembersAsClassMethods(string content, string unexpectedMethod)
    {
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "Example");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == unexpectedMethod && s.ContainerName == "Example");
    }

    [Fact]
    public void Extract_TypeScript_SemicolonlessFieldInitializerDoesNotHideComputedOrGeneratorMethods()
    {
        var content = """
            export class Example {
                field = foo
                [Symbol.iterator](): Iterator<number> {}
                *generate(): Iterable<number> {}
                next(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "[Symbol.iterator]" && s.ContainerName == "Example" && s.ReturnType == "Iterator<number>");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "generate" && s.ContainerName == "Example" && s.ReturnType == "Iterable<number>");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "next" && s.ContainerName == "Example" && s.ReturnType == "void");
    }

    [Fact]
    public void Extract_TypeScript_DetectsInlineMultipleMethodsWithObjectReturnType()
    {
        var content = """export class Example { first(): { value: string } { return { value: "x" }; } second(): void {} }""";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        var first = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "first"));
        var second = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "second"));
        Assert.Equal("first(): { value: string } { return { value: \"x\" }; }", first.Signature);
        Assert.Equal("second(): void {}", second.Signature);
    }

    [Fact]
    public void Extract_TypeScript_DetectsInlineMultipleMethodsWithConditionalObjectReturnType()
    {
        var content = """export class Example { first(): T extends U ? { a: string } : { b: string } {} second(): void {} }""";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        var first = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "first"));
        var second = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "second"));
        Assert.Equal("first(): T extends U ? { a: string } : { b: string } {}", first.Signature);
        Assert.Equal("T extends U ? { a: string } : { b: string }", first.ReturnType);
        Assert.Equal("second(): void {}", second.Signature);
        Assert.Equal("void", second.ReturnType);
    }

    [Fact]
    public void Extract_TypeScript_DetectsInlineMultipleMethodsWithFunctionReturningObjectType()
    {
        var content = """export class Example { first(): (() => { value: string }) {} second(): void {} }""";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        var first = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "first"));
        var second = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "second"));
        Assert.Equal("first(): (() => { value: string }) {}", first.Signature);
        Assert.Equal("(() => { value: string })", first.ReturnType);
        Assert.Equal("second(): void {}", second.Signature);
        Assert.Equal("void", second.ReturnType);
    }

    [Fact]
    public void Extract_TypeScript_DetectsMultilineClassMethodHeaders()
    {
        var content = """
            export class MultiLineMethod {
                run(
                    value: string,
                ): void {}

                keep(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "MultiLineMethod");
        var run = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "run"));
        var keep = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "keep"));
        Assert.Equal("void", run.ReturnType);
        Assert.Contains("run(", run.Signature);
        Assert.Contains("): void {", run.Signature);
        Assert.Equal("void", keep.ReturnType);
    }

    [Fact]
    public void Extract_TypeScript_DetectsMultilineGenericClassMethodHeaders()
    {
        var content = """
            export class MultiLineGenericMethod {
                run<T>(
                    value: T,
                ): Promise<T> {
                    return Promise.resolve(value);
                }

                keep(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "MultiLineGenericMethod");
        var run = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "run"));
        Assert.Equal("Promise<T>", run.ReturnType);
        Assert.Contains("run<T>(", run.Signature);
        Assert.Contains("): Promise<T> {", run.Signature);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ReturnType == "void");
    }

    [Fact]
    public void Extract_TypeScript_PreservesReturnTypeMetadataForMethodsWithMultilineBodies()
    {
        var content = """
            export class Example {
                run(): void {
                    return;
                }

                build(): (() => { value: string }) {
                    return () => ({ value: "x" });
                }

                async *stream<T>(): AsyncGenerator<T> {
                    yield default(T)!;
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var run = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "run"));
        var build = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "build"));
        var stream = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "stream"));
        Assert.Equal("void", run.ReturnType);
        Assert.Equal("(() => { value: string })", build.ReturnType);
        Assert.Equal("AsyncGenerator<T>", stream.ReturnType);
    }

    [Fact]
    public void Extract_TypeScript_DetectsInlineClassExpressionMethods()
    {
        var content = "const Service = class NamedService { run(): void {} };";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        var run = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "run"));
        Assert.Equal("class", run.ContainerKind);
        Assert.Equal("Service", run.ContainerName);
    }

    [Fact]
    public void Extract_TypeScript_DetectsParenthesizedClassExpressionMethods()
    {
        var content = "const Service = (class { run(): void {} });";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "Service");
    }

    [Fact]
    public void Extract_TypeScript_DetectsExportEqualsClassExpressionMethods()
    {
        var content = """
            export = class {
                run(): void {}
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_TypeScript_DetectsExportEqualsNamedClassExpressionMethods()
    {
        var content = """
            export = class Named {
                run(): void {}
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Named");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_TypeScript_DetectsParenthesizedExportEqualsClassExpressionMethods()
    {
        var content = """
            export = (class {
                run(): void {}
            });
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_TypeScript_DetectsMultilineParenthesizedExportEqualsClassSignature()
    {
        var content = """
            export =
            (
                class {
                    run(): void {}
                }
            );
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default" && s.Signature == "export = ( class {");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_TypeScript_DetectsClassExpressionInsideNamespaceBlock()
    {
        var content = """
            namespace Foo {
                export const Service = class {
                    run(): void {}
                };
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "Foo");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "Service");
    }

    [Fact]
    public void Extract_TypeScript_DetectsDollarPrefixedClassExpressionBindings()
    {
        var content = """
            export const $Service = class {
                run(): void {}
            };

            module.exports.$Handler = class {
                keep(): void {}
            };

            export namespace PublicNs {
                export const $Worker = class {
                    job(): void {}
                };
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "$Service");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "$Service");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "$Handler");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "$Handler");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "PublicNs");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "$Worker");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "job" && s.ContainerName == "$Worker");
    }

    [Fact]
    public void Extract_TypeScript_DetectsMultilineClassExpressionInsideNamespaceBlock()
    {
        var content = """
            namespace Foo {
                export const Service =
                    class {
                        run(): void {}
                    };
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "Foo");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "Service");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service" && s.Signature == "export const Service = class {");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotLeakNonExportedNamespaceClasses()
    {
        var content = """
            namespace Foo {
                class Hidden {
                    run(): void {}
                }

                const HiddenExpr = class {
                    keep(): void {}
                };

                export class Visible {
                    stay(): void {}
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "Foo");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "HiddenExpr");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "keep");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Visible");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "stay" && s.ContainerName == "Visible");
    }

    [Fact]
    public void Extract_TypeScript_DetectsParenthesizedCommonJsModuleExportsClassExpressionMethods()
    {
        var content = """
            module.exports = (class {
                run(): void {}
            });
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotLeakBlockScopedClassesInsideConditionalBlocks()
    {
        var content = """
            if (flag) {
                const Hidden = class {
                    run(): void {}
                };

                class LocalDecl {
                    keep(): void {}
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "LocalDecl");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "keep");
    }

    [Fact]
    public void Extract_TypeScript_DetectsGenericClassMethods()
    {
        var content = """
            export class Example {
                first<T extends Foo<Bar>>(): void {}
                second(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second");
    }

    [Fact]
    public void Extract_TypeScript_DetectsGenericClassMethodsWithFunctionTypeDefault()
    {
        var content = """
            export class Example {
                method<T = () => void>(): number {}
                keep(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "method" && s.ReturnType == "number");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep");
    }

    [Fact]
    public void Extract_TypeScript_DetectsGenericClassMethodsWithFunctionTypeConstraint()
    {
        var content = """
            export class Example {
                method<T extends () => void>(): number {}
                keep(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "method" && s.ReturnType == "number");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotMergeOverloadSignaturesIntoImplementationMethod()
    {
        var content = """
            class Overloaded {
                foo(x: string): string;
                foo(x: number): number;
                foo(x: string | number): string | number {
                    return x;
                }

                bar(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Overloaded");
        var fooDeclarations = symbols.Where(s => s.Kind == "function" && s.Name == "foo" && s.BodyStartLine == null).ToList();
        Assert.Equal(2, fooDeclarations.Count);
        var foo = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "foo" && s.BodyStartLine != null));
        Assert.Equal(4, foo.Line);
        Assert.Equal("string | number", foo.ReturnType);
        Assert.Equal("foo(x: string | number): string | number {", foo.Signature);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "bar" && s.ReturnType == "void");
    }

    [Fact]
    public void Extract_TypeScript_DetectsGenericClassMethodsWithMultilineFunctionTypeParameters()
    {
        var content = """
            export class Example {
                method<T = () => void>(
                    value: T,
                ): number {
                    return 1;
                }

                constrained<T extends () => void>(
                    value: T,
                ): number {
                    return 2;
                }

                keep(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "method" && s.ReturnType == "number");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "constrained" && s.ReturnType == "number");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ReturnType == "void");
    }

    [Fact]
    public void Extract_TypeScript_DetectsInlineGenericClassMethods()
    {
        var content = """export class Example { first<T extends Foo<Bar>>(): void {} second(): void {} }""";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotLeakClassMethodLocalClassExpressionMethods()
    {
        var content = """
            export class Outer {
                method(): void {
                    const Inner = class {
                        run(): void {}
                    };
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Outer");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "method" && s.ContainerName == "Outer");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Inner");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotLeakClassMethodLocalSyntheticClassExpressionsWithObjectReturnType()
    {
        var content = """
            export class Outer {
                method(): { value: string } {
                    var Service = class Hidden {
                        run(): void {}
                    };
                    module.exports = class ModuleHidden {
                        keep(): void {}
                    };
                    return { value: "x" };
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Outer");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "method" && s.ContainerName == "Outer");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "keep");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotLeakClassMethodDirectLocalClasses()
    {
        var content = """
            export class Outer {
                method(): void {
                    class Hidden {
                        run(): void {}
                    }
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Outer");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "method" && s.ContainerName == "Outer");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotLeakFunctionLocalClassExpressionMethods()
    {
        var content = """
            function outer(): void {
                const Service = class {
                    run(): void {}
                };
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "outer");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotLeakDirectFunctionLocalClasses()
    {
        var content = """
            function outer(): void {
                class Hidden {
                    run(): void {}
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "outer");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotLeakCommonJsFunctionExpressionLocalClassMethods()
    {
        var content = """
            exports.handler = function (): void {
                const Local = class {
                    inside(): void {}
                };
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Local");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "inside");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotLeakIifeLocalClassMethods()
    {
        var content = """
            (() => {
                const Local = class {
                    inside(): void {}
                };
            })();
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Local");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "inside");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotLeakBlocklessArrowReturnedClasses()
    {
        var content = """
            const factory = () =>
                class Hidden {
                    method(): void {}
                };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "factory");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "method");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotLeakWrappedBlocklessArrowReturnedClassesAndKeepsRange()
    {
        var content = """
            const factory = () =>
                wrap(
                    class Hidden {
                        method(): void {}
                    }
                );
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var factory = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "factory"));
        Assert.Equal(1, factory.StartLine);
        Assert.Equal(6, factory.EndLine);
        Assert.Equal(2, factory.BodyStartLine);
        Assert.Equal(6, factory.BodyEndLine);
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "method");
    }

    [Fact]
    public void Extract_TypeScript_BlocklessArrowWithoutSemicolonDoesNotConsumeFollowingTopLevelClass()
    {
        var content = """
            const factory = () =>
                class Hidden {
                    method(): void {}
                }
            export class Visible {
                keep(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var factory = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "factory"));
        Assert.Equal(1, factory.StartLine);
        Assert.Equal(4, factory.EndLine);
        Assert.Equal(2, factory.BodyStartLine);
        Assert.Equal(4, factory.BodyEndLine);
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Visible");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "Visible");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "method");
    }

    [Fact]
    public void Extract_TypeScript_BlocklessArrowWithoutSemicolonDoesNotConsumeFollowingExpressionStatement()
    {
        var content = """
            const factory = () =>
                class Hidden {
                    method(): void {}
                }
            foo();
            export class Visible {
                keep(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var factory = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "factory"));
        Assert.Equal(4, factory.EndLine);
        Assert.Equal(4, factory.BodyEndLine);
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Visible");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "Visible");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
    }

    [Fact]
    public void Extract_TypeScript_BlocklessArrowWithoutSemicolonDoesNotHideFollowingCommonJsClassExport()
    {
        var content = """
            const factory = () =>
                class Hidden {
                    method(): void {}
                }
            exports.Service = class Visible {
                keep(): void {}
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var factory = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "factory"));
        Assert.Equal(4, factory.EndLine);
        Assert.Equal(4, factory.BodyEndLine);
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep" && s.ContainerName == "Service");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
    }

    [Fact]
    public void Extract_TypeScript_DetectsMultilineAnonymousDefaultExportClassMembers()
    {
        var content = """
            export default class
            implements Runnable
            {
                run(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerName == "default");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotLeakStaticBlockLocalClassMethods()
    {
        var content = """
            export class Outer {
                static {
                    const Local = class {
                        inside(): void {}
                    };
                }

                keep(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Outer");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Local");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "inside");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotLeakDirectStaticBlockLocalClasses()
    {
        var content = """
            export class Outer {
                static {
                    class Local {
                        inside(): void {}
                    }
                }

                keep(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Outer");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "keep");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Local");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "inside");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotLeakObjectLiteralConciseMethodLocalClasses()
    {
        var content = """
            const obj = {
                method(): void {
                    class Inner {
                        run(): void {}
                    }
                }
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Inner");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotLeakObjectLiteralConciseMethodSyntheticClassExpressionsWithObjectReturnType()
    {
        var content = """
            const obj = {
                method(): { value: string } {
                    var Service = class Hidden {
                        run(): void {}
                    };
                    module.exports = class ModuleHidden {
                        keep(): void {}
                    };
                    return { value: "x" };
                }
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "default");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "keep");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotLeakGetterSetterLocalClasses()
    {
        var content = """
            const obj = {
                get value(): number {
                    class HiddenGetter {
                        run(): void {}
                    }
                    return 1;
                },
                set value(input: number) {
                    class HiddenSetter {
                        run(): void {}
                    }
                }
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "HiddenGetter");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "HiddenSetter");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_TypeScript_PreservesGetterSetterSignaturesVisibilityAndMethodNamedGetSet()
    {
        var content = """
            class Example {
                public get value(): number {}
                private set value(input: number) {}
                get(): void {}
                set(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "value" && s.Signature == "public get value(): number {}" && s.Visibility == "public" && s.ReturnType == "number");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "value" && s.Signature == "private set value(input: number) {}" && s.Visibility == "private");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "get" && s.Signature == "get(): void {}" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "set" && s.Signature == "set(): void {}" && s.ReturnType == "void");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotLeakNamedClassExpressionsAfterColon()
    {
        var content = """
            const pick = flag ? value : class Hidden { method(): void {} };
            const obj = { field: class Inner { run(): void {} } };
            class Visible { ok(): void {} }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Inner");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "method");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Visible");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ok" && s.ContainerName == "Visible");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotTreatControlFlowBlocksAsFunctions()
    {
        var content = """
            export class Parser {
                override parse(value: Payload): Result {
                    if (value.ready) {
                    }

                    for (const item of value.items) {
                    }

                    while (value.more) {
                    }

                    switch (value.mode) {
                        case "fast":
                            return value.result;
                    }
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Parser");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "parse");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "if");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "for");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "while");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "switch");
    }

    [Fact]
    public void Extract_TypeScript_AllowsKeywordNamedMethodsAtClassBodyDepthZero()
    {
        var content = """
            export class KeywordMethods {
                if(): void {}
                catch(): string {
                    return "ok";
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "KeywordMethods");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "if");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "catch");
    }

    [Fact]
    public void Extract_TypeScript_IgnoresRegexLiteralBracesAndBlockCommentMethodShapes()
    {
        var content = """
            export class Example {
                /*
                    fake(): void {
                    }
                */
                first(): void {
                    const open = /{/;
                    const close = /}/;
                }

                second(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "fake");
    }

    [Fact]
    public void Extract_TypeScript_IgnoresHeaderGenericBracesBeforeClassBody()
    {
        var content = """
            export class Derived extends Base<{ value: string }> {
                run(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Derived");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotTreatHeaderComparisonAsGenericAngleDepth()
    {
        var content = """
            export class Derived extends mixin(a < b ? Base : Fallback) {
                run(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Derived");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_TypeScript_FunctionRangeIgnoresComparisonAngleBracketsInParameters()
    {
        var content = """
            function choose(value = a < b ? one : two): Result {
                return value;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var choose = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "choose"));
        Assert.Equal(1, choose.StartLine);
        Assert.Equal(3, choose.EndLine);
        Assert.Equal(1, choose.BodyStartLine);
        Assert.Equal(3, choose.BodyEndLine);
    }

    [Fact]
    public void Extract_TypeScript_FunctionRangeIgnoresObjectReturnTypeBraces()
    {
        var content = """
            function outer(): { a: number } {
                return { a: 1 };
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var outer = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "outer"));
        Assert.Equal(1, outer.StartLine);
        Assert.Equal(3, outer.EndLine);
        Assert.Equal(1, outer.BodyStartLine);
        Assert.Equal(3, outer.BodyEndLine);
        Assert.Equal("function outer(): { a: number } {", outer.Signature);
    }

    [Theory]
    [InlineData(
        "typescript",
        """
        function outer({ value }) {
            var Service = class Hidden {
                run() {}
            };
        }
        """)]
    [InlineData(
        "typescript",
        """
        function outer(value: { a: number }) {
            var Service = class Hidden {
                run() {}
            };
        }
        """)]
    [InlineData(
        "typescript",
        """
        const outer = function(value = { a: 1 }) {
            var Service = class Hidden {
                run() {}
            };
        };
        """)]
    public void Extract_TypeScript_DoesNotLeakClassExpressionsFromClassicFunctionHeaders(string lang, string content)
    {
        var symbols = SymbolExtractor.Extract(1, lang, content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
    }

    [Fact]
    public void Extract_TypeScript_KeepsSiblingMethodsAfterWrappedControlFlowRegexLiterals()
    {
        var content = """
            export class Example {
                first(value: string): void {
                    if (
                        ready
                    ) /{/.test(value);
                }

                second(value: string): void {
                    if (first) {
                    }
                    else if (
                        secondReady
                    ) /{/.test(value);
                }

                third(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "third");
    }

    [Fact]
    public void Extract_TypeScript_KeepsSiblingMethodsAfterElseRegexLiteral()
    {
        var content = """
            export class Example {
                first(value: string): void {
                    if (cond) {
                    }
                    else /{/.test(value);
                }

                second(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second");
    }

    [Fact]
    public void Extract_TypeScript_KeepsSiblingMethodsAfterDoAndFinallyRegexLiterals()
    {
        var content = """
            export class Example {
                first(value: string): void {
                    do /{/.test(value); while (cond);
                }

                second(value: string): void {
                    try {
                    }
                    finally /{/.test(value);
                }

                third(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "third");
    }

    [Fact]
    public void Extract_CSharp_DetectsClassesAndMethods()
    {
        var content = "public class UserService\n{\n    public async Task<User> GetUser(int id)\n    {\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetUser");
    }

    [Fact]
    public void Extract_CSharp_NormalizesVerbatimIdentifiers()
    {
        var content = """
            namespace CsVerbatimIdent;

            public class @class
            {
                public int @int { get; set; }
                public string @return => string.Empty;
                public static @class Make() => new @class();
                public void @if() { }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "class");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "int");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "return");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Make");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "if");
        Assert.DoesNotContain(symbols, s => s.Name.StartsWith("@", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_CSharp_NormalizesVerbatimQualifiedNamespaceAndImportNames()
    {
        var content = """
            using Outer.@class;

            namespace Outer.@class
            {
                public class Container
                {
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var namespaceSymbol = Assert.Single(symbols.Where(s => s.Kind == "namespace"));
        var importSymbol = Assert.Single(symbols.Where(s => s.Kind == "import"));
        var containerSymbol = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Container"));

        Assert.Equal("Outer.class", namespaceSymbol.Name);
        Assert.Equal("Outer.class", importSymbol.Name);
        Assert.Equal("namespace", containerSymbol.ContainerKind);
        Assert.Equal("Outer.class", containerSymbol.ContainerName);
        Assert.DoesNotContain(symbols, s => s.Name.Contains("@", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_CSharp_NormalizesVerbatimConversionOperatorTargetTypes()
    {
        var content = """
            using System.Collections.Generic;

            namespace Outer.@class
            {
                public class Target
                {
                }
            }

            public class @class
            {
            }

            public class Source
            {
                public static implicit operator @class(Source value) => new @class();
                public static explicit operator Outer.@class.Target(Source value) => new Outer.@class.Target();
                public static explicit operator List<@class>(Source value) => new();
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "implicit operator class");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "explicit operator Outer.class.Target");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "explicit operator List<class>");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name.Contains("@", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_CSharp_RawStringFixturesDoNotLeakPhantomSymbols()
    {
        var content = """""
            public class FixtureHost
            {
                public void UsesRawFixture()
                {
                    const string fixture = """
                        public class App
                        {
                            public void Run()
                            {
                            }
                        }

                        function main()
                        end
                        """;

                    const string wider = """"
                        public class Wider
                        """";
                }
            }
            """"";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var method = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "UsesRawFixture"));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "FixtureHost");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "App");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Wider");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Run");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "main");
        Assert.Equal(20, method.EndLine);
        Assert.Equal(20, method.BodyEndLine);
    }

    [Fact]
    public void Extract_CSharp_NestedRawStringInsideInterpolation_DoesNotLeakPhantomSymbols()
    {
        var content = """""
            public class FixtureHost
            {
                public int Run() => 1;
                public string Id(string value) => value;

                public int UsesRawFixture()
                {
                    return $"""
                        value = {Id("""
                            public class Phantom
                            {
                                public void Go() { }
                            }
                            """) + Run()}
                        """.Length;
                }
            }
            """"";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, symbol => symbol.Kind == "class" && symbol.Name == "FixtureHost");
        Assert.Contains(symbols, symbol => symbol.Kind == "function" && symbol.Name == "UsesRawFixture");
        Assert.Contains(symbols, symbol => symbol.Kind == "function" && symbol.Name == "Run");
        Assert.Contains(symbols, symbol => symbol.Kind == "function" && symbol.Name == "Id");
        Assert.DoesNotContain(symbols, symbol => symbol.Kind == "class" && symbol.Name == "Phantom");
        Assert.DoesNotContain(symbols, symbol => symbol.Kind == "function" && symbol.Name == "Go");
    }

    [Fact]
    public void Extract_CSharp_InterpolatedVerbatimStringWithEscapedBraces_DoesNotLeakPhantomSymbols()
    {
        var content = """
            public class FixtureHost
            {
                public string Render()
                {
                    return $@"{{
                        public class Phantom
                    }}";
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, symbol => symbol.Kind == "class" && symbol.Name == "FixtureHost");
        Assert.Contains(symbols, symbol => symbol.Kind == "function" && symbol.Name == "Render");
        Assert.DoesNotContain(symbols, symbol => symbol.Kind == "class" && symbol.Name == "Phantom");
    }

    [Fact]
    public void Extract_CSharp_Issue363RawInterpolatedAndVerbatimStrings_DoNotLeakPhantomSymbols()
    {
        // Regression for issue #363 exact repro: code-shaped members inside C# raw,
        // interpolated raw, and multi-line verbatim strings must not be indexed as
        // real symbols. The current main branch already handles this correctly; this
        // test locks the user-reported fixture in place so future refactors cannot
        // silently reopen it.
        // issue #363 の exact repro 回帰: C# の raw string / 補間付き raw string /
        // 複数行 verbatim string 内のコード風メンバーを本物の symbol として
        // index してはならない。現行 main では直っているため、このテストで
        // ユーザー報告フィクスチャを固定し、将来の refactor での再発を防ぐ。
        var content = """""
            namespace CsRawStringPhantom;

            public class Svc
            {
                public int RealMethod() => 0;

                public string DocsExample() => """
                    public void FakeMethod() { }
                    public int FakeProp { get; set; }
                    public class FakeClass { }
                    public interface IFakeIface { }
                    public delegate int FakeDel();
                    public event System.EventHandler FakeEvent;
                    public Foo() { }
                    """;

                public string VerbatimExample() => @"
                    public void VerbatimFake() { }
                ";

                public string InterpExample() => $"""
                    public void InterpFake() { }
                    """;

                public int AnotherReal() => 1;
            }
            """"";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, symbol => symbol.Kind == "namespace" && symbol.Name == "CsRawStringPhantom");
        Assert.Contains(symbols, symbol => symbol.Kind == "class" && symbol.Name == "Svc");
        Assert.Contains(symbols, symbol => symbol.Kind == "function" && symbol.Name == "RealMethod");
        Assert.Contains(symbols, symbol => symbol.Kind == "function" && symbol.Name == "DocsExample");
        Assert.Contains(symbols, symbol => symbol.Kind == "function" && symbol.Name == "VerbatimExample");
        Assert.Contains(symbols, symbol => symbol.Kind == "function" && symbol.Name == "InterpExample");
        Assert.Contains(symbols, symbol => symbol.Kind == "function" && symbol.Name == "AnotherReal");
        Assert.Equal(7, symbols.Count);

        Assert.DoesNotContain(symbols, symbol => symbol.Name == "FakeMethod");
        Assert.DoesNotContain(symbols, symbol => symbol.Name == "FakeProp");
        Assert.DoesNotContain(symbols, symbol => symbol.Name == "FakeClass");
        Assert.DoesNotContain(symbols, symbol => symbol.Name == "IFakeIface");
        Assert.DoesNotContain(symbols, symbol => symbol.Name == "FakeDel");
        Assert.DoesNotContain(symbols, symbol => symbol.Name == "FakeEvent");
        Assert.DoesNotContain(symbols, symbol => symbol.Name == "Foo");
        Assert.DoesNotContain(symbols, symbol => symbol.Name == "VerbatimFake");
        Assert.DoesNotContain(symbols, symbol => symbol.Name == "InterpFake");
    }

    [Fact]
    public void Extract_CSharp_PlainInterpolatedStringHoleWithNestedStringLiteral_DoesNotDropLaterHelpers()
    {
        var content = """
            public class Fixture
            {
                private static string BuildSql(string tableName)
                {
                    var sql = $"PRAGMA index_list('{tableName.Replace("'", "''")}')";
                    return sql;
                }

                private static int ParseFoldVersion()
                {
                    return 1;
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, symbol => symbol.Kind == "class" && symbol.Name == "Fixture");
        Assert.Contains(symbols, symbol => symbol.Kind == "function" && symbol.Name == "BuildSql");
        Assert.Contains(symbols, symbol => symbol.Kind == "function" && symbol.Name == "ParseFoldVersion");
    }

    [Fact]
    public void Extract_CSharp_CommentedTripleQuotesDoNotHideFollowingMembers()
    {
        var content = "public class FixtureHost\n{\n    // \"\"\" this is only a comment marker\n    public void Run() { }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "FixtureHost");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Run");
    }

    [Fact]
    public void Extract_CSharp_CharLiteralBraceDoesNotBreakFollowingContainerAssignment()
    {
        var content = """
            namespace Demo;

            public class FixtureHost
            {
                public bool IsClosingBrace(char c)
                {
                    return c is not '}';
                }

                public void AfterBraceLiteral()
                {
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var host = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "FixtureHost"));
        var after = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "AfterBraceLiteral"));

        Assert.Equal(13, host.EndLine);
        Assert.Equal("class", after.ContainerKind);
        Assert.Equal("FixtureHost", after.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_MultilineRawStringBraceDoesNotBreakFollowingContainerAssignment()
    {
        var content = """"
            namespace Demo;

            public class FixtureHost
            {
                public string UsesRawFixture()
                {
                    return """
                        }
                        """;
                }

                public void AfterRawString()
                {
                }
            }
            """";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var host = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "FixtureHost"));
        var uses = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "UsesRawFixture"));
        var after = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "AfterRawString"));

        Assert.Equal(15, host.EndLine);
        Assert.Equal(10, uses.EndLine);
        Assert.Equal("class", after.ContainerKind);
        Assert.Equal("FixtureHost", after.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_MultilineVerbatimStringBraceDoesNotBreakFollowingRangeDetection()
    {
        var content = """"
            namespace Demo;

            public class FixtureHost
            {
                public string UsesVerbatimFixture()
                {
                    return @"
            {
            ";
                }

                public void AfterVerbatimString()
                {
                }
            }
            """";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var host = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "FixtureHost"));
        var uses = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "UsesVerbatimFixture"));
        var after = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "AfterVerbatimString"));

        Assert.Equal(15, host.EndLine);
        Assert.Equal(10, uses.EndLine);
        Assert.Equal("class", after.ContainerKind);
        Assert.Equal("FixtureHost", after.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_InterpolatedStringCallSites_DoNotEmitPhantomDescribeStateDefinitions()
    {
        // Regression for issue #790: method-call text inside interpolation holes of an
        // outer multi-line string must not be stitched into fake `function` declarations.
        // The real declaration should remain queryable, but call-site fragments from the
        // log string must not surface as extra `DescribeState` definitions.
        // issue #790 の回帰: 外側の複数行文字列にある interpolation hole 内のメソッド呼び出し
        // テキストを、偽の `function` 宣言として継ぎ合わせてはならない。本物の宣言は
        // 取得できるままにしつつ、ログ文字列由来の call-site 断片は追加の
        // `DescribeState` 定義として現れてはならない。
        var content = """""
            namespace Demo;

            public sealed class ReporterContext
            {
                public string ReportsFolderAbsolutePath { get; set; } = string.Empty;
            }

            public sealed class AuditLogGenerateService
            {
                internal static string DescribeState(string label, string? pathOrCommand)
                    => $"{label}:{pathOrCommand}";

                public void WriteAuditLog(ReporterContext context, string auditLogPath)
                {
                    var message = $"""
                        Failed to write audit log for reports folder {context.ReportsFolderAbsolutePath}
                        to {auditLogPath}
                        current state {
                            DescribeState("ReportsFolder", context.ReportsFolderAbsolutePath)}
                        """;
                }
            }
            """"";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var describeState = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "DescribeState"));
        Assert.Equal("AuditLogGenerateService", describeState.ContainerName);
        Assert.Equal("string", describeState.ReturnType);
        Assert.Equal(
            """internal static string DescribeState(string label, string? pathOrCommand) => $"{label}:{pathOrCommand}";""",
            describeState.Signature);
    }

    [Fact]
    public void Extract_CSharp_DetectsFileScopedNamespaceAndRecordStruct()
    {
        // C# 10+: file-scoped namespace, global using, record struct
        var content = "global using System.Text;\nnamespace MyApp.Models;\n\npublic record struct Point(int X, int Y);\n\npublic record class User(string Name);";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name.Contains("System.Text"));
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "MyApp.Models");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Point");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
    }

    [Fact]
    public void Extract_CSharp_DetectsDelegateAndEvent()
    {
        // C# delegate and event / C# デリゲートとイベント
        var content = "public delegate void EventHandler(object sender, EventArgs e);\n\npublic class Button\n{\n    public event EventHandler Click;\n    public static event Action<string> OnLog;\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "delegate" && s.Name == "EventHandler");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Button");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "Click");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "OnLog");
    }

    [Fact]
    public void Extract_CSharp_DetectsSpacedGenericDelegatesAndEvents()
    {
        var content = """
            namespace App;
            using System;

            public delegate Task<int> GetIdAsync(string user);
            public delegate Task<Dictionary<string, int>> LoadAsync();
            public delegate TResult Func<T1, T2, TResult>(T1 a, T2 b);

            public class Pub
            {
                public event Action<string, int> NamedEvent;
                public event Func<string, int, bool> Filter;
                public event EventHandler<ChangedArgs> Changed;
                public event Action OnReady;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "delegate" && s.Name == "GetIdAsync" && s.ReturnType == "Task<int>");
        Assert.Contains(symbols, s => s.Kind == "delegate" && s.Name == "LoadAsync" && s.ReturnType == "Task<Dictionary<string,int>>");
        Assert.Contains(symbols, s => s.Kind == "delegate" && s.Name == "Func" && s.ReturnType == "TResult");

        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "NamedEvent" && s.ReturnType == "Action<string,int>");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "Filter" && s.ReturnType == "Func<string,int,bool>");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "Changed" && s.ReturnType == "EventHandler<ChangedArgs>");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "OnReady" && s.ReturnType == "Action");
        Assert.DoesNotContain(symbols, s => s.Kind == "event" && s.Name == "int");
    }

    [Fact]
    public void Extract_CSharp_DetectsProperties()
    {
        // C# property with get/set / C# プロパティ（get/set付き）
        var content = "public class User\n{\n    public string Name { get; set; }\n    public int Age { get; init; }\n    public virtual string? Email { get; set; }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Name");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Age");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Email");
    }

    [Fact]
    public void Extract_CSharp_DetectsPartialProperties()
    {
        var content = """
            namespace Demo;

            public abstract partial class BaseModel
            {
                public abstract string Description { get; }
            }

            public partial class Model : BaseModel
            {
                public partial string Name
                {
                    get;
                    set;
                }

                public partial int Count
                {
                    get;
                }

                public string NotPartial { get; set; } = string.Empty;
            }

            public partial class Model
            {
                private string _name = string.Empty;

                public partial string Name
                {
                    get => _name;
                    set => _name = value;
                }

                public partial int Count
                    => 42;

                public partial override string Description
                    => "demo";
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.True(symbols.Count(s => s.Kind == "property" && s.Name == "Name") >= 2);
        Assert.True(symbols.Count(s => s.Kind == "property" && s.Name == "Count") >= 2);
        Assert.True(symbols.Count(s => s.Kind == "property" && s.Name == "Description") >= 2);
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "NotPartial");

        var countImplementation = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Count" && s.Line == 34));
        Assert.Equal(34, countImplementation.StartLine);
        Assert.Equal(35, countImplementation.EndLine);

        var descriptionImplementation = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Description" && s.Line == 37));
        Assert.Equal(37, descriptionImplementation.StartLine);
        Assert.Equal(38, descriptionImplementation.EndLine);
    }

    [Fact]
    public void Extract_CSharp_DetectsReadonlyProperties()
    {
        // issue #327: `readonly` is a valid property/accessor modifier on C# 8+ struct
        // members. All three shapes — expression-bodied (`readonly int A => _v;`),
        // auto-property (`readonly int B { get; }`), and accessor-body
        // (`readonly int C { get => _v; }`) — must surface as `property` rows. The regex
        // modifier slot must consume `readonly` so that a standalone accessor line
        // (`readonly get => _v;`) inside a block-bodied property does NOT match the
        // expression-bodied property regex and leak a phantom `property get` / `property set`.
        // issue #327: C# 8+ 構造体メンバーの `readonly` は property/accessor 修飾子として有効。
        // 式本体 (`readonly int A => _v;`)、自動プロパティ (`readonly int B { get; }`)、
        // accessor-body (`readonly int C { get => _v; }`) の三形態はいずれも `property`
        // として抽出される必要がある。regex の修飾子スロットが `readonly` を消費することで、
        // ブロック本体プロパティ内の `readonly get => _v;` accessor 行が単独で式本体プロパティ
        // regex にマッチせず phantom `property get` / `property set` を生まない。
        var content = """
            namespace Demo;

            public struct S
            {
                private int _v;

                public readonly int A => _v;
                public readonly int B { get; }
                public readonly int C { get => _v; }

                public int Mixed
                {
                    readonly get => _v;
                    set => _v = value;
                }

                public int D { get; set; }
                public readonly int GetD() => D;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var a = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "A"));
        Assert.Equal("int", a.ReturnType);
        Assert.Equal("public", a.Visibility);

        var b = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "B"));
        Assert.Equal("int", b.ReturnType);
        Assert.Equal("public", b.Visibility);

        var c = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "C"));
        Assert.Equal("int", c.ReturnType);
        Assert.Equal("public", c.Visibility);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Mixed");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "D");

        // Baseline: `readonly` methods continue to extract as `function`.
        // ベースライン: `readonly` メソッドは従来どおり `function` として抽出される。
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetD");

        // Phantom suppression: neither the accessor line `readonly get => _v;` nor the
        // accessor line `set => _v = value;` must leak a top-level `property` row named
        // `get` / `set` / `init`.
        // phantom 抑止: accessor 行の `readonly get => _v;` や `set => _v = value;` が
        // top-level の `property get` / `property set` / `property init` を生まないこと。
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "get");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "set");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "init");
    }

    [Fact]
    public void Extract_CSharp_DetectsReadonlyIndexers()
    {
        // issue #352: `readonly` is a valid indexer modifier on C# 8+ struct members.
        // Expression-body (`public readonly int this[int i] => _arr[i];`), block-body
        // (`public readonly string this[string key] { get => key; }`), and generic
        // (`public readonly T this[int i] { get => _items[i]; }`) shapes must all be
        // captured as `function` rows with C#'s metadata name `Item` and `visibility` /
        // `returnType` populated, not dropped because `readonly` is missing from the
        // indexer-regex modifier slot. The non-readonly baseline indexer and the
        // readonly method baseline must continue to extract as before.
        // issue #352: C# 8+ 構造体メンバーの `readonly` はインデクサ修飾子として有効。
        // 式本体 (`public readonly int this[int i] => _arr[i];`)、ブロック本体
        // (`public readonly string this[string key] { get => key; }`)、ジェネリック
        // (`public readonly T this[int i] { get => _items[i]; }`) のいずれも、C# メタ
        // データ名 `Item` の `function` 行として `visibility` / `returnType` を保持
        // したまま抽出される必要がある。インデクサ regex の修飾子スロットに `readonly`
        // が無いことで silent drop されてはならない。非 readonly なインデクサと
        // readonly メソッドのベースラインは従来どおり抽出される。
        var content = """
            namespace Demo;

            public struct S
            {
                private int[] _arr;

                public readonly int this[int i] => _arr[i];

                public readonly string this[string key]
                {
                    get => key;
                }

                public int this[long key] => 0;

                public readonly int ReadMe() => 0;
            }

            public struct Bag<T>
            {
                private T[] _items;

                public readonly T this[int i] { get => _items[i]; }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var indexerItems = symbols.Where(s => s.Kind == "function" && s.Name == "Item").ToList();
        Assert.Equal(4, indexerItems.Count);

        // Expression-body readonly indexer (int) — visibility and returnType preserved.
        // 式本体 readonly インデクサ (int) — visibility と returnType を保持。
        var exprReadonlyInt = Assert.Single(indexerItems.Where(s => s.ReturnType == "int" && s.Signature != null && s.Signature.Contains("this[int i]")));
        Assert.Equal("public", exprReadonlyInt.Visibility);
        Assert.Contains("readonly", exprReadonlyInt.Signature);

        // Block-body readonly indexer (string key) — visibility and returnType preserved.
        // ブロック本体 readonly インデクサ (string key) — visibility と returnType を保持。
        var blockReadonlyString = Assert.Single(indexerItems.Where(s => s.ReturnType == "string"));
        Assert.Equal("public", blockReadonlyString.Visibility);
        Assert.Contains("readonly", blockReadonlyString.Signature);

        // Non-readonly baseline indexer (int this[long key]).
        // 非 readonly ベースラインインデクサ (int this[long key])。
        var baselineIntLong = Assert.Single(indexerItems.Where(s => s.ReturnType == "int" && s.Signature != null && s.Signature.Contains("this[long key]")));
        Assert.Equal("public", baselineIntLong.Visibility);
        Assert.DoesNotContain("readonly", baselineIntLong.Signature);

        // Generic readonly indexer (`public readonly T this[int i] { get => _items[i]; }`).
        // ジェネリック readonly インデクサ。
        var genericReadonly = Assert.Single(indexerItems.Where(s => s.ReturnType == "T"));
        Assert.Equal("public", genericReadonly.Visibility);
        Assert.Contains("readonly", genericReadonly.Signature);

        // Baseline: `readonly` methods continue to extract as `function` with the source name.
        // ベースライン: `readonly` メソッドは従来どおり `function` として抽出される。
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ReadMe" && s.ReturnType == "int");
    }

    [Fact]
    public void Extract_CSharp_DetectsPartialIndexers()
    {
        // issue #350: `partial` is a valid indexer modifier as of C# 13's extended partial
        // member support. Expression-body (`public partial int this[int i] => _arr[i];`),
        // block-body (`public partial string this[string key] { get => key; }`), and
        // partial-implementation (`public partial int this[long key] => 0;`) shapes must all
        // be captured as `function` rows with C#'s metadata name `Item` and `visibility` /
        // `returnType` populated, not dropped because `partial` is missing from the indexer
        // regex modifier slot. The non-partial baseline indexer must continue to extract as
        // before.
        // issue #350: C# 13 の partial member 拡張で `partial` はインデクサ修飾子として有効。
        // 式本体 (`public partial int this[int i] => _arr[i];`)、ブロック本体
        // (`public partial string this[string key] { get => key; }`)、実装側 partial
        // (`public partial int this[long key] => 0;`) のいずれも、C# メタデータ名 `Item` の
        // `function` 行として `visibility` / `returnType` を保持したまま抽出される必要が
        // ある。インデクサ regex の修飾子スロットに `partial` が無いことで silent drop されて
        // はならない。非 partial なインデクサは従来どおり抽出される。
        var content = """
            namespace Demo;

            public partial class Store
            {
                public partial int this[int i] { get; }

                public partial string this[string key]
                {
                    get;
                }

                public int this[long key] => 0;
            }

            public partial class Store
            {
                private int[] _arr = new int[0];
                private System.Collections.Generic.Dictionary<string, string> _map = new();

                public partial int this[int i] => _arr[i];

                public partial string this[string key]
                {
                    get => _map[key];
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var indexerItems = symbols.Where(s => s.Kind == "function" && s.Name == "Item").ToList();
        Assert.Equal(5, indexerItems.Count);

        // Two partial indexer declarations with `int` return type (declaration + implementation).
        // `int` 戻り値型の partial インデクサ宣言は 2 件 (宣言 + 実装) 検出される。
        var partialIntIndexers = indexerItems.Where(s => s.ReturnType == "int" && s.Signature != null && s.Signature.Contains("this[int i]")).ToList();
        Assert.Equal(2, partialIntIndexers.Count);
        Assert.All(partialIntIndexers, s => Assert.Equal("public", s.Visibility));
        Assert.All(partialIntIndexers, s => Assert.Contains("partial", s.Signature));

        // Two partial indexer declarations with `string` return type (declaration + implementation).
        // `string` 戻り値型の partial インデクサ宣言も 2 件 (宣言 + 実装) 検出される。
        var partialStringIndexers = indexerItems.Where(s => s.ReturnType == "string").ToList();
        Assert.Equal(2, partialStringIndexers.Count);
        Assert.All(partialStringIndexers, s => Assert.Equal("public", s.Visibility));
        Assert.All(partialStringIndexers, s => Assert.Contains("partial", s.Signature));

        // Non-partial baseline indexer (int this[long key]).
        // 非 partial ベースラインインデクサ (int this[long key])。
        var baselineIntLong = Assert.Single(indexerItems.Where(s => s.Signature != null && s.Signature.Contains("this[long key]")));
        Assert.Equal("public", baselineIntLong.Visibility);
        Assert.Equal("int", baselineIntLong.ReturnType);
        Assert.DoesNotContain("partial", baselineIntLong.Signature);
    }

    [Fact]
    public void Extract_CSharp_DetectsPartialEvents()
    {
        // issue #350: `partial` is a valid event modifier as of C# 14's partial event support.
        // Field-like partial events (`public partial event Action E;`) and accessor-based
        // partial events (`public partial event Action<int> OnLog { add { ... } remove { ... } }`)
        // must be captured as `event` rows with `visibility` / `returnType` populated, not
        // dropped because `partial` is missing from the event-regex modifier slot. Non-partial
        // events must continue to extract as before.
        // issue #350: C# 14 の partial event サポートで `partial` は event 修飾子として有効。
        // field-like partial event (`public partial event Action E;`) と accessor ベースの
        // partial event (`public partial event Action<int> OnLog { add { ... } remove { ... } }`)
        // のいずれも、`event` 行として `visibility` / `returnType` を保持したまま抽出される必要が
        // ある。event regex の修飾子スロットに `partial` が無いことで silent drop されては
        // ならない。非 partial event は従来どおり抽出される。
        var content = """
            namespace Demo;

            public partial class Emitter
            {
                public partial event System.Action Click;
                public partial event System.Action<string> OnLog;
                public event System.Action Plain;
            }

            public partial class Emitter
            {
                public partial event System.Action Click { add { } remove { } }
                public partial event System.Action<string> OnLog { add { } remove { } }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        // Two partial event declarations with name `Click` (declaration + accessor-body implementation).
        // 名前 `Click` の partial event 宣言が 2 件 (宣言 + アクセサ本体実装) 検出される。
        var clickEvents = symbols.Where(s => s.Kind == "event" && s.Name == "Click").ToList();
        Assert.Equal(2, clickEvents.Count);
        Assert.All(clickEvents, s => Assert.Equal("public", s.Visibility));
        Assert.All(clickEvents, s => Assert.Contains("partial", s.Signature ?? string.Empty));

        // Two partial event declarations with name `OnLog` (generic Action<string>).
        // 名前 `OnLog` の partial event 宣言 (ジェネリック Action<string>) も 2 件検出される。
        var onLogEvents = symbols.Where(s => s.Kind == "event" && s.Name == "OnLog").ToList();
        Assert.Equal(2, onLogEvents.Count);
        Assert.All(onLogEvents, s => Assert.Equal("public", s.Visibility));
        Assert.All(onLogEvents, s => Assert.Contains("partial", s.Signature ?? string.Empty));

        // Non-partial baseline event (`public event System.Action Plain;`) still extracts.
        // 非 partial ベースライン event (`public event System.Action Plain;`) は従来どおり抽出。
        var plain = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "Plain"));
        Assert.Equal("public", plain.Visibility);
        Assert.DoesNotContain("partial", plain.Signature ?? string.Empty);
    }

    [Fact]
    public void Extract_CSharp_MultilinePropertyHeader_DoesNotCreatePhantomFunctionAndKeepsSignature()
    {
        var content = """
            namespace Demo;

            public class Model
            {
                public string
                    SplitName
                    => "x";
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var property = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "SplitName"));
        Assert.Equal(5, property.StartLine);
        Assert.Equal(7, property.EndLine);
        Assert.Equal("public string SplitName => \"x\";", property.Signature);
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "SplitName");
    }

    [Fact]
    public void Extract_CSharp_LongGenericMultilinePropertyHeader_KeepsReturnTypeAndSignature()
    {
        var content = """
            namespace Demo;

            public class Model
            {
                public Dictionary<
                    string,
                    List<
                        int
                    >>
                    Count
                    => new();
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var property = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Count"));
        Assert.Equal("Dictionary<string,List<int>>", property.ReturnType);
        Assert.Equal("public Dictionary< string, List< int >> Count => new();", property.Signature);
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Count");
    }

    [Fact]
    public void Extract_CSharp_BraceOnNextLinePropertyHeader_KeepsHeaderSignature()
    {
        var content = """
            namespace Demo;

            public class Model
            {
                public string SplitName
                {
                    get;
                    set;
                }

                public int Count
                { get => 1; }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var splitName = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "SplitName"));
        var count = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Count"));
        Assert.Equal("public string SplitName {", splitName.Signature);
        Assert.Equal("public int Count {", count.Signature);
    }

    [Fact]
    public void Extract_CSharp_WrappedClassHeader_IncludesBaseListAndWhereClauseInSignature()
    {
        var content = """
            namespace Demo;

            public sealed class Foo<T>
                : BaseFoo<T>, IBar, IBaz
                where T : class, new()
            {
                public Foo(int x) : base(x) { }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var foo = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Foo"));
        Assert.Equal(
            "public sealed class Foo<T> : BaseFoo<T>, IBar, IBaz where T : class, new()",
            foo.Signature);
    }

    [Fact]
    public void Extract_CSharp_WrappedInterfaceHeader_IncludesBaseListInSignature()
    {
        var content = """
            namespace Demo;

            public interface IFoo<T>
                : IBar<T>,
                  IBaz
                where T : struct
            {
                void Method();
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var iface = Assert.Single(symbols.Where(s => s.Kind == "interface" && s.Name == "IFoo"));
        Assert.Equal(
            "public interface IFoo<T> : IBar<T>, IBaz where T : struct",
            iface.Signature);
    }

    [Fact]
    public void Extract_CSharp_WrappedRecordPrimaryCtorHeader_IncludesCtorParametersAndBaseList()
    {
        var content = """
            namespace Demo;

            public record Point<T>(
                T X,
                T Y)
                : BaseRecord<T>
                where T : INumber<T>;
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var point = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Point"));
        Assert.Equal(
            "public record Point<T>( T X, T Y) : BaseRecord<T> where T : INumber<T>",
            point.Signature);
    }

    [Fact]
    public void Extract_CSharp_WrappedStructHeader_IncludesBaseListInSignature()
    {
        var content = """
            namespace Demo;

            public readonly struct Value<T>
                : IEquatable<Value<T>>
                where T : IComparable<T>
            {
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var value = Assert.Single(symbols.Where(s => s.Kind == "struct" && s.Name == "Value"));
        Assert.Equal(
            "public readonly struct Value<T> : IEquatable<Value<T>> where T : IComparable<T>",
            value.Signature);
    }

    [Fact]
    public void Extract_CSharp_WrappedEnumHeader_IncludesUnderlyingTypeInSignature()
    {
        var content = """
            namespace Demo;

            public enum Kind
                : byte
            {
                A,
                B,
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var kind = Assert.Single(symbols.Where(s => s.Kind == "enum" && s.Name == "Kind"));
        Assert.Equal("public enum Kind : byte", kind.Signature);
    }

    [Fact]
    public void Extract_CSharp_NewEnumModifier_ExtractsEnumSymbol()
    {
        // Closes #353: nested `new enum` (member hiding in derived type) must be captured.
        // Modifier order is free, so both `public new enum` and `new public enum` work, and
        // an explicit underlying-type colon must still classify as kind `enum`.
        // Closes #353: 派生型で親のネスト enum を隠蔽する `new enum` は enum としてキャプチャする。
        // 修飾子の順序は自由で、`public new enum` と `new public enum` の両方、
        // 明示的な基底型指定 `: byte` が付いた場合でも kind `enum` として分類する。
        var content = """
            namespace Demo;

            public class Derived : Base
            {
                public new enum Kind { A }
                public new enum KindByte : byte { A }
                new public enum KindReversed { A }
                new enum KindPrivate { A }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Kind" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "KindByte" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "KindReversed" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "KindPrivate");
    }

    [Fact]
    public void Extract_CSharp_NewDelegateModifier_ExtractsDelegateSymbol()
    {
        // Regression for #353 companion: nested `new delegate` (member hiding in derived type)
        // must stay captured. Modifier order is free, so both `public new delegate` and
        // `new public delegate` work.
        // #353 関連の回帰テスト: 派生型で親のネスト delegate を隠蔽する `new delegate` は
        // delegate としてキャプチャし続ける。修飾子の順序は自由で、`public new delegate` と
        // `new public delegate` の両方を受け付ける。
        var content = """
            namespace Demo;

            public class Derived : Base
            {
                public new delegate int Handler(int x);
                new public delegate int Reversed();
                new delegate int PrivateD();
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "delegate" && s.Name == "Handler" && s.Visibility == "public" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "delegate" && s.Name == "Reversed" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "delegate" && s.Name == "PrivateD");
    }

    [Fact]
    public void Extract_CSharp_SameLineClassHeader_SignatureUnchanged()
    {
        var content = """
            namespace Demo;

            public class Foo : Bar, IBaz
            {
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var foo = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Foo"));
        Assert.Equal("public class Foo : Bar, IBaz", foo.Signature);
    }

    [Fact]
    public void Extract_CSharp_WrappedClassHeaderWithLineComment_StripsCommentFromSignature()
    {
        // Wrapped type header with a trailing `// comment` on a base-list or `where` line
        // must not leak comment text into `symbols.signature`. The signature is used by
        // downstream consumers (planned #257 base resolution, #256 type-position
        // references, `impact` / `analyze_symbol` heuristics) that need to parse the base
        // list and `where` clauses; comment bytes in the signature would break them.
        // Closes #382 codex review blocker.
        // 折り返された型ヘッダの base リストや `where` 句の行末に `// comment` が
        // 付いていても、`symbols.signature` にコメント本文が漏れないこと。signature は
        // 下流（#257 の base 解決、#256 の型位置参照、`impact` / `analyze_symbol`
        // ヒューリスティクス）で base リストや `where` 句を解釈するために使われるため、
        // コメントバイトが残ると壊れる。Closes #382 の codex レビュー blocker 対応。
        var content = """
            namespace Demo;

            public sealed class Foo<T>
                : BaseFoo<T>, // primary base
                  IBar,
                  IBaz // diagnostics trait
                where T : class, new() // must be default-constructible
            {
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var foo = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Foo"));
        Assert.Equal("public sealed class Foo<T> : BaseFoo<T>, IBar, IBaz where T : class, new()", foo.Signature);
    }

    [Fact]
    public void Extract_CSharp_WrappedClassHeaderWithBlockComment_StripsCommentFromSignature()
    {
        // Same contract as the line-comment variant, for inline `/* ... */` block
        // comments embedded inside a wrapped type header. Closes #382 codex review blocker.
        // 行間や途中に挟まる `/* ... */` ブロックコメントについても同じ契約を固定する。
        // Closes #382 の codex レビュー blocker 対応。
        var content = """
            namespace Demo;

            public class Foo /* annotation */
                : /* base */ Bar,
                  IBaz
                where /* generic */ T : class
            {
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var foo = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Foo"));
        Assert.Equal("public class Foo : Bar, IBaz where T : class", foo.Signature);
    }

    [Fact]
    public void Extract_CSharp_WrappedPrimaryCtorHeaderWithStringDefault_PreservesWhitespaceInLiteral()
    {
        // A wrapped primary constructor header that carries a string default with internal
        // double-space must not collapse the literal into a single space. The signature is
        // parsed downstream to recover default values, so collapsing `"a  b"` to `"a b"`
        // would silently rewrite source. Closes #382 codex review iteration 2 blocker.
        // 折り返された primary constructor header に内部 2 連空白を持つ文字列デフォルトが
        // ある場合、リテラル内の空白を潰してはいけない。signature は下流で default 値の
        // 復元に使われるため、`"a  b"` が `"a b"` に潰れると source が書き換わったのと
        // 同じ結果になる。Closes #382 の codex レビュー iteration 2 blocker 対応。
        var content = """
            namespace Demo;

            public sealed class Foo(
                string label = "a  b")
                : BaseFoo
            {
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var foo = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Foo"));
        Assert.Equal(
            "public sealed class Foo( string label = \"a  b\") : BaseFoo",
            foo.Signature);
    }

    [Fact]
    public void Extract_CSharp_WrappedPrimaryCtorHeaderWithVerbatimStringDefault_PreservesWhitespaceInLiteral()
    {
        // Verbatim string (`@"..."`) defaults may contain runs of internal whitespace that
        // must survive signature reconstruction verbatim. Closes #382 codex review iteration
        // 2 blocker.
        // verbatim 文字列（`@"..."`）のデフォルトは内部の空白列をそのまま残す必要がある。
        // Closes #382 の codex レビュー iteration 2 blocker 対応。
        var content = """"
            namespace Demo;

            public sealed class Foo(
                string path = @"C:\tmp\   spaces")
                : BaseFoo
            {
            }
            """";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var foo = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Foo"));
        Assert.Equal(
            "public sealed class Foo( string path = @\"C:\\tmp\\   spaces\") : BaseFoo",
            foo.Signature);
    }

    [Fact]
    public void Extract_CSharp_WrappedPrimaryCtorHeaderWithRawStringDefault_PreservesWhitespaceInLiteral()
    {
        // Raw string literals (`"""..."""`) in a wrapped primary constructor default must
        // preserve internal whitespace verbatim. Closes #382 codex review iteration 2
        // blocker.
        // raw 文字列リテラル（`"""..."""`）を持つ primary constructor デフォルトについて
        // も、内部空白を verbatim に保つこと。Closes #382 の codex レビュー iteration 2
        // blocker 対応。
        var content = """"
            namespace Demo;

            public sealed class Foo(
                string tag = """a   b""")
                : BaseFoo
            {
            }
            """";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var foo = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Foo"));
        Assert.Equal(
            "public sealed class Foo( string tag = \"\"\"a   b\"\"\") : BaseFoo",
            foo.Signature);
    }

    [Fact]
    public void Extract_CSharp_WrappedPrimaryCtorHeaderWithMultilineRawStringDefault_PreservesNewlinesAndIndent()
    {
        // A raw string default that spans multiple physical lines must keep its `\n`
        // characters and the per-line leading indentation verbatim. The previous
        // line-by-line `Trim()` + `' '` join in BuildCSharpTypeHeaderSignature destroyed
        // both, collapsing `"""\n    a  \n    b\n    """` into `""" a b """`. Closes #382
        // codex review iteration 3 blocker.
        // 折り返された primary constructor のデフォルトに multi-line raw string を置くと、
        // 改行と各行先頭のインデントを verbatim に保持しなければならない。以前の line-by-line
        // `Trim()` + ' ' 連結は両方を潰し `"""\n    a  \n    b\n    """` を `""" a b """`
        // に圧縮していた。Closes #382 の codex レビュー iteration 3 blocker 対応。
        var content = """"
            namespace Demo;

            public sealed class Foo(
                string text = """
                a  b
                c
                """)
                : BaseFoo
            {
            }
            """";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var foo = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Foo"));
        Assert.Equal(
            "public sealed class Foo( string text = \"\"\"\n    a  b\n    c\n    \"\"\") : BaseFoo",
            foo.Signature);
    }

    [Fact]
    public void Extract_CSharp_WrappedMultilineRawStringDefault_NormalizesCrlfToLf()
    {
        // Content split on '\n' leaves trailing '\r' on every line for CRLF-terminated
        // sources (Windows CI with autocrlf=true, files saved from VS, etc.). The header
        // slice builder must strip that trailing '\r' so inter-line separators stay '\n'
        // regardless of line endings. Without this normalization the signature for a
        // multi-line raw string default would carry `\r\n` between lines on Windows and
        // `\n` on Linux / macOS, which breaks signature equality across OSes and broke
        // the Windows CI run of #382.
        // `\n` で分割した場合、CRLF 終端のソースでは各行末に '\r' が残る
        // （autocrlf=true の Windows CI、VS で保存したファイルなど）。header スライス組み
        // 立て側で末尾 '\r' を落とさないと、行間セパレータが OS に依存して `\r\n` / `\n`
        // になり、signature の一致判定が崩れる。これは #382 の Windows CI 失敗の原因でも
        // あった。
        var content =
            "namespace Demo;\r\n" +
            "\r\n" +
            "public sealed class Foo(\r\n" +
            "    string text = \"\"\"\r\n" +
            "    a  b\r\n" +
            "    c\r\n" +
            "    \"\"\")\r\n" +
            "    : BaseFoo\r\n" +
            "{\r\n" +
            "}\r\n";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var foo = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Foo"));
        Assert.Equal(
            "public sealed class Foo( string text = \"\"\"\n    a  b\n    c\n    \"\"\") : BaseFoo",
            foo.Signature);
    }

    [Fact]
    public void Extract_CSharp_MultilineEnumMemberSpan_NormalizesCrlfToLf()
    {
        // Enum members whose value expression or attribute block crosses physical lines go
        // through GetSourceSpanText via TryAddCSharpEnumMemberFromSpan. Content is split on
        // '\n', so CRLF sources leave '\r' on every non-final line and the concatenated
        // signature would contain '\r\n' between physical lines on Windows. Pin this to '\n'
        // so signature equality is OS-independent (#405 follow-up to #382).
        // enum メンバーの値式や属性ブロックが行を跨ぐ場合、TryAddCSharpEnumMemberFromSpan
        // 経由で GetSourceSpanText に入る。content は '\n' で分割しているため、CRLF ソース
        // では各行末に '\r' が残り、Windows では signature に '\r\n' が混入していた。OS
        // 差分で signature が変わらないよう '\n' に揃える（#382 に続く #405 対応）。
        var content =
            "public enum Modes\r\n" +
            "{\r\n" +
            "    Red = 1 |\r\n" +
            "        2,\r\n" +
            "    Blue = 3,\r\n" +
            "}\r\n";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var red = Assert.Single(symbols.Where(s => s.Kind == "enum" && s.Name == "Red"));
        Assert.NotNull(red.Signature);
        Assert.DoesNotContain('\r', red.Signature);
        Assert.Contains("\n", red.Signature);
    }

    [Fact]
    public void Extract_JavaScript_WrappedBareMethodHeader_NormalizesCrlfToLf()
    {
        // JS/TS class-body methods whose header wraps across physical lines go through
        // TryCaptureJavaScriptTypeScriptMethodHeader, which appends each line with a '\n'
        // prefix. Without CRLF normalization, Windows sources (autocrlf=true, VS saves)
        // produce a Signature carrying '\r\n' between lines. Pin to '\n' for OS-independent
        // signature equality (#405 follow-up to #382).
        // JS/TS の class body method で header が行を跨ぐ場合、
        // TryCaptureJavaScriptTypeScriptMethodHeader が各行を '\n' 接頭辞で連結する。
        // CRLF 正規化がないと Windows ソース（autocrlf=true、VS 保存など）で Signature に
        // '\r\n' が混入していた。OS 差分で一致判定が崩れないよう '\n' に揃える
        // （#382 に続く #405 対応）。
        var content =
            "class Foo {\r\n" +
            "    myMethod(\r\n" +
            "        a,\r\n" +
            "        b,\r\n" +
            "    ) {\r\n" +
            "    }\r\n" +
            "}\r\n";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        var method = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "myMethod"));
        Assert.NotNull(method.Signature);
        Assert.DoesNotContain('\r', method.Signature);
        Assert.Contains("\n", method.Signature);
    }

    [Fact]
    public void Extract_CSharp_MultilineRecordPrimaryComponents_SurviveCrlfInput()
    {
        // Record primary constructors with a wrapped component list feed
        // CollectRecordDeclarationText, which appends each physical line with a '\n' prefix.
        // Without CRLF normalization, the collected declaration text carries '\r' in the
        // middle, which — while parsing still scans only for structural characters — breaks
        // text-equality assumptions downstream. Pin the property extraction to succeed on
        // CRLF input so the fix stays tied to observable behavior (#405 follow-up to #382).
        // record の primary constructor で component リストが行を跨ぐ場合、
        // CollectRecordDeclarationText が各行を '\n' 接頭辞で連結する。CRLF 正規化がないと
        // collected text に '\r' が混じり、parsing 自体は構造文字しか見ないものの、下流の
        // 文字列比較前提が崩れる。CRLF 入力でも property 抽出が壊れないことを固定し、修正
        // を観測可能な挙動に紐づける（#382 に続く #405 対応）。
        var content =
            "namespace App;\r\n" +
            "\r\n" +
            "public record Point(\r\n" +
            "    int X,\r\n" +
            "    int Y);\r\n";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var x = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "X" && s.ContainerName == "Point"));
        Assert.Equal("int", x.ReturnType);
        var y = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Y" && s.ContainerName == "Point"));
        Assert.Equal("int", y.ReturnType);
    }

    [Fact]
    public void Extract_CSharp_WrappedHeaderWithInterpolationHoleContainingNestedVerbatim_PreservesInnerLiteral()
    {
        // An interpolation hole in an outer `$"..."` must be classified as Code so the
        // hole contents are lex-aware — in particular, a nested `@"..."` inside the hole
        // must stay in Verbatim mode and preserve any internal double-space, while the
        // outer `$"..."` literal content after the hole is still preserved verbatim.
        // Previously, once we entered String mode we exited on the first unescaped `"`,
        // which meant `$"{@"a  b"}  c"` re-entered Code mode at `@"` and collapsed
        // `a  b` to `a b`. Closes #382 codex review iteration 3 blocker.
        // 外側 `$"..."` の補間ホールは Code として分類し、ホール内は lex-aware に処理する
        // 必要がある。ホール内の `@"..."` は Verbatim モードとして扱い、内部の 2 連空白を
        // 保持することを固定する。以前は String に入った時点で次の `"` で即 Code に戻って
        // いたため、`$"{@"a  b"}  c"` が `a  b` → `a b` に潰れていた。
        // Closes #382 の codex レビュー iteration 3 blocker 対応。
        var content = """
            namespace Demo;

            public sealed class Foo
                : BaseFoo($"{@"a  b"}  c")
            {
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var foo = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Foo"));
        Assert.Equal(
            "public sealed class Foo : BaseFoo($\"{@\"a  b\"}  c\")",
            foo.Signature);
    }

    [Fact]
    public void Extract_CSharp_NonPartialBlockStyleProperty_AccessorVariants_AreCaptured()
    {
        // Regression coverage for #229: non-partial properties with `{` on the next line
        // and each major accessor body style (auto, expression-bodied arrows, `init`,
        // full method bodies) must all surface as property symbols with header-aligned
        // start lines and end lines spanning the closing brace.
        // #229 の回帰ガード: 非 partial プロパティで `{` が次行に来るすべての代表的な
        // accessor 本体スタイル（auto / `get =>` `set =>` / `init` / フル本体）を、
        // header 行を起点に閉じブレースまでを含む property として抽出し続けることを固定する。
        var content = """
            namespace Demo;

            public class Model
            {
                public string BlockAuto
                {
                    get;
                    set;
                }

                public string BlockFull
                {
                    get => _x;
                    set => _x = value;
                }

                public int BlockInit
                {
                    get;
                    init;
                }

                public string BlockWithLogic
                {
                    get
                    {
                        return _x;
                    }
                    set
                    {
                        _x = value ?? "";
                    }
                }

                private string _x = "";
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var blockAuto = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "BlockAuto"));
        Assert.Equal(5, blockAuto.StartLine);
        Assert.Equal(9, blockAuto.EndLine);

        var blockFull = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "BlockFull"));
        Assert.Equal(11, blockFull.StartLine);
        Assert.Equal(15, blockFull.EndLine);

        var blockInit = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "BlockInit"));
        Assert.Equal(17, blockInit.StartLine);
        Assert.Equal(21, blockInit.EndLine);

        var blockWithLogic = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "BlockWithLogic"));
        Assert.Equal(23, blockWithLogic.StartLine);
        Assert.Equal(33, blockWithLogic.EndLine);

        // None of the block-style variants should leak as phantom functions with the same name.
        // どのブロックスタイルも同名の phantom function として重複抽出されてはいけない。
        Assert.DoesNotContain(symbols, s => s.Kind == "function"
            && (s.Name == "BlockAuto" || s.Name == "BlockFull" || s.Name == "BlockInit" || s.Name == "BlockWithLogic"));
    }

    [Fact]
    public void Extract_CSharp_MultilineExpressionBodiedProperty_KeepsExpressionBodyRange()
    {
        var content = """
            namespace Demo;

            public partial class Model
            {
                public partial int Count
                    => DateTime.Now.Day switch
                    {
                        > 15 => 2,
                        _ => 1
                    };
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var count = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Count"));
        Assert.Equal(5, count.StartLine);
        Assert.Equal(10, count.EndLine);
        Assert.Equal("public partial int Count => DateTime.Now.Day switch { > 15 => 2, _ => 1 };", count.Signature);
    }

    [Fact]
    public void Extract_CSharp_PartialPropertyImplementation_WithAccessorAttribute_IsDetected()
    {
        var content = """
            namespace Demo;

            public partial class Model
            {
                public partial string Name { get; set; }
            }

            public partial class Model
            {
                public partial string Name { [System.Obsolete] get => "x"; set { } }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Equal(2, symbols.Count(s => s.Kind == "property" && s.Name == "Name"));
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Name" && s.Signature != null && s.Signature.Contains("[System.Obsolete]", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_CSharp_PartialPropertyImplementation_WithMultilineAccessorAttribute_IsDetected()
    {
        var content = """
            namespace Demo;

            public partial class Model
            {
                public partial string Name
                {
                    [System.Obsolete(
                        "x"
                    )]
                    get => "x";
                    set { }
                }

                public int Other => 1;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Name");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Other");
    }

    [Fact]
    public void Extract_CSharp_PropertyWithFirstAccessorVisibility_IsDetected()
    {
        // issue #332: `public int X { internal get; set; }` と、`{ private get; public set; }` /
        // `{ protected internal get; set; }` / `{ private protected get; set; }` のように
        // 先頭の accessor に独自の可視性修飾子が付く形も property として抽出されること。
        // accessor の属性プレフィックス (`[Obsolete]` / `[field: NonSerialized]`)、
        // accessor 本体付き (`internal get { ... } set { ... }`)、単独の accessor
        // (`{ private init; }`) も同じパスで拾えることを併せて固定する。
        // issue #332: properties whose FIRST accessor carries its own visibility
        // modifier (`{ internal get; set; }`, `{ private get; public set; }`,
        // `{ protected internal get; set; }`, `{ private protected get; set; }`)
        // must still be captured as properties. Also pins attribute-prefixed
        // accessors (`[Obsolete]` / `[field: NonSerialized]`), body-bearing
        // accessors (`internal get { ... } set { ... }`), and a standalone
        // accessor with visibility (`{ private init; }`).
        var content = """
            using System;
            namespace AccessorVis;

            public class Svc
            {
                public int PubPrivSet { get; private set; }
                public string Name { get; private init; } = "";
                public int Count { get; protected set; }
                public int Internal { internal get; set; }
                public int PrivGetPubSet { private get; public set; }
                public int ProtIntGet { protected internal get; set; }
                public int PrivProtGet { private protected get; set; }
                public int AttrFirstAccessor { [Obsolete] internal get; set; }
                public int FieldAttrFirstAccessor { [field: NonSerialized] private get; set; }
                public int BodyBearing { internal get { return 0; } set { _ = value; } }
                public int PrivInitOnly { private init; }
                public int Prop => 0;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var expected = new[]
        {
            "PubPrivSet",
            "Name",
            "Count",
            "Internal",
            "PrivGetPubSet",
            "ProtIntGet",
            "PrivProtGet",
            "AttrFirstAccessor",
            "FieldAttrFirstAccessor",
            "BodyBearing",
            "PrivInitOnly",
            "Prop",
        };
        foreach (var name in expected)
            Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == name));

        // The first-accessor-visibility rows must not leak as phantom functions either.
        // 先頭 accessor 可視性付きの行が phantom function としても重複抽出されないこと。
        var phantomCandidates = new[]
        {
            "Internal",
            "PrivGetPubSet",
            "ProtIntGet",
            "PrivProtGet",
            "AttrFirstAccessor",
            "FieldAttrFirstAccessor",
            "BodyBearing",
            "PrivInitOnly",
        };
        Assert.DoesNotContain(symbols, s => s.Kind == "function"
            && Array.IndexOf(phantomCandidates, s.Name) >= 0);
    }

    [Fact]
    public void Extract_CSharp_DetectsInlineAttributedProperty()
    {
        var content = """
            using System.Text.Json.Serialization;

            public class UserDto
            {
                [JsonPropertyName("full_name")] public string FullName { get; set; } = string.Empty;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserDto");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "FullName");
    }

    [Fact]
    public void Extract_CSharp_DetectsInlinePropertyTargetAttributeWithWhitespace()
    {
        var content = """
            using System.Text.Json.Serialization;

            public class UserDto
            {
                [property : JsonPropertyName("full_name")] public string FullName { get; set; } = string.Empty;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "FullName");
    }

    [Fact]
    public void Extract_CSharp_DetectsNoVisibilityMembers()
    {
        // Classes/methods without explicit visibility (internal by default)
        // 明示的な visibility のないクラス/メソッド（デフォルト internal）
        var content = "namespace MyApp;\n\nclass InternalClass\n{\n    static void Helper() { }\n}\n\nstatic class Utils\n{\n    static int Add(int a, int b) => a + b;\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "InternalClass");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Helper");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Utils");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Add");
    }

    [Fact]
    public void Extract_CSharp_DetectsPrimaryConstructors()
    {
        // C# 12 primary constructors on class, struct, record
        // C# 12 のクラス・構造体・record の primary constructor
        var content = "public class Service(ILogger logger, IDb db)\n{\n}\n\npublic struct Point(double x, double y);";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Point");
        // Signature should contain the parameter list / シグネチャにパラメータリストが含まれるべき
        var service = symbols.First(s => s.Name == "Service");
        Assert.Contains("ILogger logger", service.Signature);
    }

    [Fact]
    public void Extract_CSharp_AttributeDoesNotBlockNextLine()
    {
        // [Attribute] on the line before class/method should not prevent extraction
        // [Attribute] がクラス/メソッドの前行にあっても抽出を妨げないこと
        var content = "[Serializable]\npublic class Config\n{\n    [Obsolete(\"Use V2\")]\n    public void OldMethod() { }\n\n    [HttpGet(\"/api\")]\n    public async Task<IActionResult> GetItems() { return null; }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "OldMethod");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetItems");
    }

    [Fact]
    public void Extract_CSharp_TargetedAttributeDeclarations_DoNotBecomePhantomFunctions()
    {
        var content = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            [assembly: CLSCompliant(true)]
            [assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
            [module: SkipLocalsInit]

            public class Fixture
            {
                [return: MarshalAs(UnmanagedType.Bool)]
                public bool Method([param: MarshalAs(UnmanagedType.Bool)] bool value) => value;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Fixture");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Method");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "CLSCompliant");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "AssemblyVersion");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "SkipLocalsInit");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "MarshalAs");
    }

    [Fact]
    public void Extract_CSharp_MultiSectionAttribute_DoesNotLeakTrailingAttributeNamesAsPhantoms()
    {
        // [A, B(args)] multi-section attributes must not leak B/C as phantom `function` symbols.
        // The attribute-stripper must blank out the whole bracket group even when it consumes the entire line,
        // otherwise the method regex latches onto the content after the comma.
        // xUnit/MSTest/ASP.NET/EF attribute conventions rely heavily on this shape.
        // 複数セクション属性 [A, B(args)] の 2つ目以降の属性名が phantom function として漏れないこと。
        var content = """
            using System;
            using System.Diagnostics;

            namespace MultiSectionAttr;

            public class Svc
            {
                [Obsolete, Conditional("DEBUG")]
                public void A() {}

                [Obsolete][Conditional("DEBUG")]
                public void B() {}

                [Obsolete] [Conditional("DEBUG")]
                public void C() {}

                [Obsolete, Conditional("DEBUG")]
                [System.ComponentModel.Description("x")]
                public void D() {}

                [Obsolete, Conditional("DEBUG"), System.ComponentModel.Description("y")]
                public void E() {}

                [Fact, Trait("cat", "io")]
                public void F() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Svc");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "A");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "B");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "C");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "D");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "E");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "F");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Conditional");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Description");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Trait");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Obsolete");
    }

    [Fact]
    public void Extract_CSharp_MultiSectionAttributeOnClassOrProperty_DoesNotLeakPhantoms()
    {
        // Comma-separated attribute sections on types and properties (EF/ASP.NET/DataAnnotations shape)
        // must stay clean as well — [Required, StringLength(50), Column("name")] etc.
        // 型・プロパティに付く [Required, StringLength(50), Column("name")] 形でも phantom が出ないこと。
        var content = """
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            namespace Data;

            [Serializable, ApiController]
            public class User
            {
                [Required, StringLength(50), Column("name")]
                public string Name { get; set; } = "";

                [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
                public int Id { get; set; }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Name");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Id");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "StringLength");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Column");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "DatabaseGenerated");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "ApiController");
    }

    [Fact]
    public void Extract_CSharp_WrappedConstructorInitializer_DoesNotLeakBaseOrThisAsPhantoms()
    {
        // Wrapped `: base(...)` / `: this(...)` initializers must not surface as phantom
        // `function base` / `function this` symbols. The C# returnType char class includes `:`
        // to support alias-qualified type names like `Alias::Type`, so a wrapped initializer line
        // like `    : base(s, 0)` could otherwise tokenize as returnType=`:` + name=`base` + paren.
        // Both the first-char `(?![?:])` guard and the name-level `(?!(?:base|this)\b)` guard
        // must cooperate to block it. Closes #331.
        // ラップされた `: base(...)` / `: this(...)` 初期化子行が `function base` / `function this`
        // の phantom として漏れないことを担保する。Closes #331.
        var content = """
            namespace CtorChain;

            public class Base
            {
                public Base() { }
                public Base(int x) { }
                public Base(string s, int n) { }
            }

            public class Derived : Base
            {
                public Derived(int x) : base(x) { }

                public Derived(string s)
                    : base(s, 0)
                {
                }

                public Derived() : this(0) { }

                public Derived(int a, int b)
                    : this(a)
                {
                }

                public Derived(double d) : base((int)d, "d") => System.Console.WriteLine(d);
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Base");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Derived");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "base");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "this");
        // All five Derived constructors should still be captured / 5 つのコンストラクタは正しく取得できること
        Assert.Equal(5, symbols.Count(s => s.Kind == "function" && s.Name == "Derived"));
    }

    [Fact]
    public void Extract_CSharp_LinqQueryExpressionContinuations_DoNotLeakPhantoms()
    {
        // LINQ query-expression continuation lines with qualified method calls
        // (e.g. `where Validator.Check(x)`, `select Mapper.Convert(x)`, `orderby Math.Abs(x)`)
        // must not fire the explicit-interface-implementation regex as
        // `returnType=<linq-keyword>` + `interface=<qualifier>` + `name=<member>`.
        // Closes #377.
        // LINQ 式の continuation 行（`where Validator.Check(x)` など）が、明示的インターフェース実装
        // regex の returnType+qualifier+name 形として一致し phantom function を生まないこと。Closes #377.
        var content = """
            using System.Linq;
            using System.Collections.Generic;

            namespace LinqPhantom;

            public static class Validator { public static bool Check(int x) => x > 0; }
            public static class Mapper { public static string Convert(int x) => x.ToString(); }

            public class Svc
            {
                public void Query()
                {
                    var list = new List<int> { 1, 2, 3 };

                    var q1 = from x in list
                             where Validator.Check(x)
                             select x;

                    var q2 = from x in list
                             select Mapper.Convert(x);

                    var q3 = from x in list
                             orderby Math.Abs(x)
                             select x;

                    // Exercise line-leading `group`, `by`, and `into` so the guard
                    // covers each keyword individually instead of only the q4 opener.
                    // 行頭 `group` / `by` / `into` を個別に踏ませ、q4 先頭だけで抜けないようにする。
                    var q4 = from x in list
                             group x
                             by Helper.Key(x)
                             into g
                             select g;

                    // Exercise line-leading `join`, `on`, and `equals` so the guard
                    // covers each keyword individually instead of only the q5 opener.
                    // 行頭 `join` / `on` / `equals` を個別に踏ませ、q5 先頭だけで抜けないようにする。
                    var q5 = from x in list
                             join y in list
                             on Helper.Key(x)
                             equals Helper.Key(y)
                             select x;

                    var q6 = from x in list
                             let doubled = Helper.Double(x)
                             select doubled;

                    // Exercise line-leading `ascending` and `descending` so the guard
                    // covers them even when an `orderby` clause wraps onto its own line.
                    // 行頭 `ascending` / `descending` を個別に踏ませ、`orderby` が折り返したときも抜けないようにする。
                    var q7 = from x in list
                             orderby Helper.Key(x)
                             ascending
                             select x;

                    var q8 = from x in list
                             orderby Helper.Key(x)
                             descending
                             select x;
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        // Real symbols should survive / 実体のシンボルは残る
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Validator");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Mapper");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Svc");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Query");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Check" && s.ContainerName == "Validator");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Convert" && s.ContainerName == "Mapper");

        // No phantom `function` symbols should appear inside the Query body / Query 本体から phantom が出ないこと
        var phantomNames = new[] { "Abs", "Key", "Double" };
        foreach (var name in phantomNames)
        {
            Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == name);
        }

        // Check and Convert must only be declared once each (on their real definition lines), not duplicated from LINQ continuations.
        // Check と Convert は定義行の1個ずつだけで、LINQ continuation からの重複が出ないこと。
        Assert.Equal(1, symbols.Count(s => s.Kind == "function" && s.Name == "Check"));
        Assert.Equal(1, symbols.Count(s => s.Kind == "function" && s.Name == "Convert"));
    }

    [Fact]
    public void Extract_CSharp_ContextualKeywordWithTupleSuffixReturn_DoesNotLeakCtorRegexPhantom()
    {
        // Before #349, `public required (int, int) R1 { get; init; }` / `public partial (int, int)? P1();`
        // / `public readonly (int, int)? M() => null;` could be claimed by the ctor regex
        // (`^\s*visibility\s+\w+\s*\(`) with the modifier keyword captured as the ctor name, emitting
        // phantom `function required` / `function partial` / `function readonly` rows and silently
        // dropping the real property/method. The ctor regex now adds a negative lookahead at the
        // opening paren that rejects lines where the matching `)` is followed by an identifier +
        // `{` / `(` / `=>` (with optional `?` / `[]` tuple suffixes in between), so the more specific
        // method/property regexes get a chance to match and no phantom is emitted. Closes #349.
        // 修飾子キーワード + tuple-suffix 戻り値の行を ctor regex が greedy に喰い、
        // modifier キーワード自体を ctor 名として拾ってしまう現象に対するガード。
        // ctor regex の開き括弧の直後に否定先読みを入れ、「対応する `)` のあとに
        // 識別子 + `{` / `(` / `=>`（間に `?` / `[]` の tuple サフィックスを許す）が続く行」を
        // 弾くようにしたので、method / property 側の regex に先を譲り phantom
        // `function required` / `function partial` / `function readonly` が出ない
        // ことを担保する。Closes #349.
        var content = """
            namespace ModifierPhantom;

            public partial class A
            {
                public partial (int, int)? P1();
                public partial (int, int)[] P2();
            }

            public class B
            {
                public required (int, int) R1 { get; init; }
                public required (int, int)? R2 { get; init; }
            }

            public class D
            {
                public readonly struct E
                {
                    public readonly (int, int)? M() => null;
                }
            }

            public class F
            {
                public F() { }
                public F(int x) { }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        // No phantom rows whose name is a modifier keyword / 修飾子キーワードを name にした phantom は出ない。
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "partial");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "required");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "readonly");

        // Real members are captured / 本物のメンバーが拾えていること。
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "P1");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "P2");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "R1");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "R2");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "M");

        // Baseline constructors must still be captured / 通常のコンストラクタは引き続き拾えること。
        Assert.Equal(2, symbols.Count(s => s.Kind == "function" && s.Name == "F"));
    }

    [Fact]
    public void Extract_CSharp_CtorRegex_StillCapturesAllValidCtorForms()
    {
        // The #349 fix tightens the ctor regex with a negative lookahead that rejects lines where
        // the matching `)` is followed by `IDENT { / IDENT ( / IDENT =>`. Any realistic ctor form
        // must still be captured after the fix — otherwise we would silently drop real ctors to
        // block phantom ones. This test locks in every major ctor form: brace body, expression
        // body, `: base(...)` / `: this(...)` initializers, `extern` declaration ending in `;`,
        // multi-line signature split across lines, and tuple parameter. A regression here means
        // the lookahead is too aggressive.
        // #349 の修正で ctor regex に否定先読み（閉じ括弧の後に `IDENT { / IDENT ( / IDENT =>`
        // が続く行を弾く）を足した。phantom を止めるために本物の ctor を落とすと本末転倒なので、
        // 主要な ctor 記法（brace 本体 / 式本体 / `: base(...)` / `: this(...)` 初期化子 /
        // `;` で終わる extern 宣言 / 複数行に分かれたシグネチャ / tuple パラメータ）が全て
        // 引き続き拾えることをここで担保する。これが壊れたら lookahead が強すぎるサイン。
        var content = """
            namespace CtorForms;

            public class Brace
            {
                public Brace() { }
                public Brace(int x) { }
            }

            public class ExpressionBody
            {
                public ExpressionBody() => System.Console.WriteLine();
            }

            public class WithInitializer
            {
                public WithInitializer() : this(0) { }
                public WithInitializer(int x) : base() { }
            }

            public class Extern
            {
                public extern Extern();
                public extern Extern(int x);
            }

            public class MultiLine
            {
                public MultiLine(
                    int x,
                    int y)
                {
                }
            }

            public class TupleParam
            {
                public TupleParam((int, int) t) { }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Equal(2, symbols.Count(s => s.Kind == "function" && s.Name == "Brace"));
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ExpressionBody");
        Assert.Equal(2, symbols.Count(s => s.Kind == "function" && s.Name == "WithInitializer"));
        Assert.Equal(2, symbols.Count(s => s.Kind == "function" && s.Name == "Extern"));
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MultiLine");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "TupleParam");
    }

    [Fact]
    public void Extract_CSharp_ContextualKeywordWithWhitespacedTupleSuffix_DoesNotLeakCtorRegexPhantom()
    {
        // Follow-up to #349: the initial positional-lookahead fix only rejected tuple suffixes
        // directly abutting `)` (e.g. `(int, int)[]`), so legal C# that puts whitespace between
        // `)` and the suffix token (`(int, int) []`, `(int, int) ?`, `(int, int)  ?`) fell
        // through to the ctor regex and reintroduced phantom `function required` / `function
        // readonly` / `function static` rows while dropping the real property / method. Both the
        // ctor lookahead and CSharpTypePattern now share CSharpTupleSuffixPattern, which allows
        // whitespace between `)` / identifier and each suffix token, so these formatting
        // variants are rejected as ctor shapes via the lookahead and accepted as property /
        // method shapes via the upstream rows.
        // #349 のフォローアップ。初回の位置検査修正では `)` とサフィックストークンが密着した形
        // （`(int, int)[]`）しか弾けず、`)` と `[]` / `?` の間に空白を置いた合法な書式
        // （`(int, int) []` / `(int, int) ?` / `(int, int)  ?`）は ctor regex に落ち、
        // phantom `function required` / `function readonly` / `function static` が再発し
        // 本来の property / method が silent drop していた。CSharpTupleSuffixPattern を
        // ctor 否定先読みと CSharpTypePattern で共有し、`)` や識別子と各サフィックストークンの
        // 間に空白を許容することで、これらの整形バリエーションも ctor 形状として弾きつつ
        // 上流の property / method 行で本物のシンボルとして拾えるようになる。
        var content = """
            namespace ModifierPhantomSpaced;

            public partial class SpacedHost
            {
                public required (int, int) [] R4 { get; init; }
                public readonly (int, int) ? F4 = null;
                public static (int, int) ? M3() => default;
                public partial (int, int)  ? P5 { get; init; }
            }

            public readonly struct SpacedStruct
            {
                public readonly (int, int) ? M4() => null;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        // No phantom rows whose name is a modifier keyword even when whitespace sits between
        // `)` and the tuple suffix. / `)` とサフィックスの間に空白があっても、修飾子キーワードを
        // name にした phantom 行は出ない。
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "required");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "readonly");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "static");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "partial");

        // Real members are still captured with the correct kinds. / 本物のメンバーが正しい kind で拾えていること。
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "R4");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "F4");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "M3");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "P5");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "M4");
    }

    [Fact]
    public void Extract_CSharp_DetectsRecordVariants()
    {
        // record, record class, record struct with various modifiers
        var content = "public record UserDto(string Name, int Age);\npublic sealed record class Config(string Key, string Value);\ninternal readonly record struct Point(double X, double Y);";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserDto");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Point");
        // Signature should contain parameters / シグネチャにパラメータが含まれるべき
        var userDto = symbols.First(s => s.Name == "UserDto");
        Assert.Contains("string Name", userDto.Signature);
    }

    [Fact]
    public void Extract_CSharp_DetectsRecordPrimaryComponentsAsProperties()
    {
        var content = """
            namespace App;

            public record Point(int X, int Y);
            public readonly record struct Vec3(double X, double Y, double Z);
            public record Animal(string Name);
            public record Dog(string Name, string Breed) : Animal(Name);
            public record Options(
                string Host,
                int Port) { public bool UseTls { get; init; } = true; }
            public record Container<T>(T Value, int Count) where T : class;
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var pointX = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "X" && s.ContainerName == "Point"));
        Assert.Equal("int", pointX.ReturnType);
        Assert.Equal("class", pointX.ContainerKind);
        Assert.Equal("App.Point", pointX.ContainerQualifiedName);
        Assert.Equal("public", pointX.Visibility);

        var vec3Z = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Z" && s.ContainerName == "Vec3"));
        Assert.Equal("double", vec3Z.ReturnType);
        Assert.Equal("struct", vec3Z.ContainerKind);

        var dogBreed = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Breed" && s.ContainerName == "Dog"));
        Assert.Equal("string", dogBreed.ReturnType);

        var optionsHost = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Host" && s.ContainerName == "Options"));
        Assert.Equal("string", optionsHost.ReturnType);
        Assert.Contains("string Host", optionsHost.Signature);
        Assert.Equal(8, optionsHost.Line);
        var optionsRecord = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Options"));
        Assert.Equal(9, optionsRecord.EndLine);

        var containerValue = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Value" && s.ContainerName == "Container"));
        Assert.Equal("T", containerValue.ReturnType);
    }

    [Fact]
    public void Extract_CSharp_DetectsLongAndCommentedRecordPrimaryComponentsAsProperties()
    {
        var componentLines = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"    int P{i},"));
        var content = $$"""
            namespace App;

            public record Big(
            {{componentLines}}
                int Tail);

            public record Point(
                int X /* separator, comment */,
                // the next component must still parse
                int Y);
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "P1" && s.ContainerName == "Big");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "P30" && s.ContainerName == "Big");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Tail" && s.ContainerName == "Big");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "X" && s.ContainerName == "Point");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Y" && s.ContainerName == "Point");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "separator" && s.ContainerName == "Point");

        var tail = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Tail" && s.ContainerName == "Big"));
        Assert.Equal(34, tail.Line);
        var bigRecord = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Big"));
        Assert.Equal(34, bigRecord.EndLine);

        var pointY = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Y" && s.ContainerName == "Point"));
        Assert.Equal(39, pointY.Line);
    }

    [Fact]
    public void Extract_CSharp_DetectsRecordPrimaryComponentsWithComparisonDefaults()
    {
        var content = """
            public record Threshold(bool Enabled = 1 < 2, int Count = 0);
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var enabled = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Enabled" && s.ContainerName == "Threshold"));
        Assert.Equal("bool", enabled.ReturnType);

        var count = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Count" && s.ContainerName == "Threshold"));
        Assert.Equal("int", count.ReturnType);
    }

    [Fact]
    public void Extract_CSharp_DetectsRecordPrimaryComponentsWithTightComparisonDefaults()
    {
        var content = """
            public record Threshold(bool Enabled = left<right, int Count = 0);
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var enabled = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Enabled" && s.ContainerName == "Threshold"));
        Assert.Equal("bool", enabled.ReturnType);

        var count = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Count" && s.ContainerName == "Threshold"));
        Assert.Equal("int", count.ReturnType);
    }

    [Fact]
    public void Extract_CSharp_DoesNotInjectRecordPrimaryComponentsIntoEarlierSameNamedClass()
    {
        var content = """
            namespace A
            {
                public class Point {}
            }

            namespace B
            {
                public record Point(int X);
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "X" && s.ContainerQualifiedName == "A.Point");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "X" && s.ContainerQualifiedName == "B.Point");
    }

    [Fact]
    public void Extract_CSharp_DetectsBodylessRecordStructDeclarationRange()
    {
        var content = """
            namespace App;

            public readonly record struct Bodyless(
                int X,
                int Y);
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var bodyless = Assert.Single(symbols.Where(s => s.Kind == "struct" && s.Name == "Bodyless"));
        Assert.Equal(5, bodyless.EndLine);

        var y = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Y" && s.ContainerName == "Bodyless"));
        Assert.Equal(5, y.Line);
        Assert.Equal("App.Bodyless", y.ContainerQualifiedName);
    }

    [Fact]
    public void Extract_CSharp_DetectsBodylessRecordBaseAndWhereDeclarationRange()
    {
        var content = """
            namespace App;

            public record Animal(string Name);

            public record Dog(
                string Name)
                : Animal(Name);

            public record Box<T>(
                T Value)
                where T : class;
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var dog = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Dog"));
        Assert.Equal(7, dog.EndLine);

        var box = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Box"));
        Assert.Equal(11, box.EndLine);
    }

    [Fact]
    public void Extract_CSharp_DetectsBodylessRecordBaseComparisonDeclarationRange()
    {
        var content = """
            namespace App;

            public record Child(int X)
                : Base(1 < 2);

            public record Base(bool Flag);
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var child = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Child"));
        Assert.Equal(4, child.EndLine);
    }

    [Fact]
    public void Extract_CSharp_DetectsBodylessRecordTightBaseComparisonDeclarationRange()
    {
        var content = """
            namespace App;

            public record Child(int X)
                : Base(left<right);

            public record Base(bool Flag);
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var child = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Child"));
        Assert.Equal(4, child.EndLine);
    }

    [Fact]
    public void Extract_CSharp_DetectsEmptyBodylessRecordDeclarationRange()
    {
        var content = """
            namespace Repro;

            public record Empty(
            )
                : Base();

            public record Base();

            public record struct EmptyStruct(
            );
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var empty = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Empty"));
        Assert.Equal(5, empty.EndLine);

        var emptyStruct = Assert.Single(symbols.Where(s => s.Kind == "struct" && s.Name == "EmptyStruct"));
        Assert.Equal(10, emptyStruct.EndLine);
    }

    [Fact]
    public void Extract_CSharp_TracksRecordPrimaryComponentLineAfterAttributes()
    {
        var content = """
            public record Person(
                [property: Obsolete]
                string Name,
                [property: Obsolete]
                int Age);
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var name = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Name" && s.ContainerName == "Person"));
        Assert.Equal(3, name.Line);

        var age = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Age" && s.ContainerName == "Person"));
        Assert.Equal(5, age.Line);
    }

    [Fact]
    public void Extract_CSharp_RecordComponents_DoNotDisruptBodyMembersOrDuplicateExplicitProperties()
    {
        var content = """
            namespace App;

            public record Person(string Name)
            {
                public string Name { get; init; } = Name;

                public string Upper() => Name.ToUpperInvariant();
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var nameProperties = symbols.Where(s => s.Kind == "property" && s.Name == "Name" && s.ContainerName == "Person").ToList();
        var nameProperty = Assert.Single(nameProperties);
        Assert.Equal(5, nameProperty.Line);
        Assert.Equal("App.Person", nameProperty.ContainerQualifiedName);

        var upper = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Upper"));
        Assert.Equal("class", upper.ContainerKind);
        Assert.Equal("Person", upper.ContainerName);
        Assert.Equal("App.Person", upper.ContainerQualifiedName);
    }

    [Fact]
    public void Extract_CSharp_DetectsCompoundVisibility()
    {
        // protected internal and private protected / 複合アクセス修飾子
        var content = "public class Base\n{\n    protected internal void Foo() { }\n    private protected int Bar { get; set; }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var foo = symbols.FirstOrDefault(s => s.Name == "Foo");
        Assert.NotNull(foo);
        Assert.Equal("protected internal", foo.Visibility);

        var bar = symbols.FirstOrDefault(s => s.Name == "Bar");
        Assert.NotNull(bar);
        Assert.Equal("private protected", bar.Visibility);
    }

    [Fact]
    public void Extract_CSharp_DetectsUsingAlias()
    {
        var content = "using System;\nusing Json = System.Text.Json;\nusing static System.Math;\nglobal using Logging = Microsoft.Extensions.Logging;";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "System");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Json");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "System.Math");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Logging");
    }

    [Fact]
    public void Extract_CSharp_DetectsExternAlias()
    {
        // extern alias is a file-prelude declaration used for assembly-alias reconciliation.
        // It must precede using directives per the C# spec.
        // Closes #326.
        var content = "extern alias CoreV1;\nextern alias CoreV2;\n    extern alias Indented;\n\nglobal using System;\nusing static System.Math;\n\nnamespace Demo;\n\npublic class Box\n{\n    public int Calc() => Max(1, 2);\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        // All three extern alias lines should be captured as import kind
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "CoreV1");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "CoreV2");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Indented");

        // Existing using forms must still capture alongside extern alias (no reshuffling)
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "System");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "System.Math");

        // The namespace/class/method should still be captured correctly
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "Demo");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Box");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Calc");
    }

    [Fact]
    public void Extract_CSharp_DetectsConstAndStaticReadonly()
    {
        var content = "public class Config\n{\n    public const string Version = \"1.0\";\n    private const int MaxRetries = 3;\n    internal static readonly Dictionary<string, string> Map = new();\n    public string MutableField;\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Version" && s.ReturnType == "string");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MaxRetries" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Map");
        // Regular mutable fields are now extracted as `property` / 通常のフィールドも `property` として抽出される
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "MutableField" && s.ReturnType == "string");
    }

    [Fact]
    public void Extract_CSharp_StaticReadonlyField_FreeModifierOrder()
    {
        // Closes #355: C# allows modifiers to appear in any order, so `readonly static`,
        // `readonly new static`, and `new readonly static` must all be captured as the
        // kind `function` row (static readonly field), not fall through to the plain-field
        // (kind `property`) row.
        // Closes #355: C# の修飾子は任意順で書けるため、`readonly static` /
        // `readonly new static` / `new readonly static` も kind `function`（static readonly
        // フィールド）として取り扱い、通常フィールド（kind `property`）に流れ落ちないこと。
        var content = """
            public class Svc
            {
                public static readonly int A = 1;
                public readonly static int B = 2;
                public new static readonly int C = 3;
                public readonly new static int D = 4;
                public new readonly static int D2 = 5;
                readonly public static int E = 6;
                static readonly new int F = 7;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "A" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "B" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "C" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "D" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "D2" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "E" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "F" && s.ReturnType == "int");
        // Each static readonly declaration must be captured exactly once — no duplicate `property` row
        // from the plain-field regex.
        // それぞれの static readonly 宣言は1回だけ捕捉する — 通常フィールド regex からの重複 `property`
        // 行を生まないこと。
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name is "A" or "B" or "C" or "D" or "D2" or "E" or "F");
    }

    [Fact]
    public void Extract_CSharp_Method_FreeModifierOrder()
    {
        // Closes #355: C# allows visibility to appear anywhere in the modifier sequence, so
        // `static public`, `static internal`, `async public`, `override public` must all be
        // captured as kind `function` with the correct visibility.
        // Closes #355: visibility は修飾子シーケンスの任意位置に置けるため、`static public` /
        // `static internal` / `async public` / `override public` もすべて kind `function` として
        // 正しい visibility で捕捉されること。
        var content = """
            public class Svc
            {
                public static int I() => 0;
                static public int F() => 0;
                static internal void G() { }
                async public System.Threading.Tasks.Task H() { return; }
                override public int J() => 0;
                virtual public int K() => 0;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "I" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "F" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "G" && s.Visibility == "internal");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "H" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "J" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "K" && s.Visibility == "public");
    }

    [Fact]
    public void Extract_CSharp_Property_ModifierBeforeVisibility()
    {
        // Closes #355: property/indexer/event/delegate/operator rows also accept visibility
        // that follows a modifier (e.g. `static public int X { get; set; }`).
        // Closes #355: property / indexer / event / delegate / operator 行も、
        // `static public int X { get; set; }` のように修飾子の後の visibility を受け付ける。
        var content = """
            public class Svc
            {
                static public int P1 { get; set; }
                static public int P2 => 0;
                virtual public int P3 { get; set; }
                override public int P4 => 0;
                static public event System.EventHandler E1;
                static public delegate int D1(int x);
                static public int this[int i] => 0;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "P1" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "P2" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "P3" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "P4" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "E1" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "delegate" && s.Name == "D1" && s.Visibility == "public");
        // Indexer is recorded as `Item` (C# metadata name) after NormalizeCSharpSymbolName.
        // インデクサは NormalizeCSharpSymbolName で C# メタデータ名 `Item` に正規化される。
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Item" && s.Visibility == "public");
    }

    [Fact]
    public void Extract_CSharp_InterfaceEventsUseInterfaceContainer()
    {
        var content = """
            using System;
            namespace EventMods;

            public interface IBus
            {
                event EventHandler Regular;
                static abstract event EventHandler StaticAbs;
                static virtual event EventHandler StaticVirt { add { } remove { } }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        foreach (var name in new[] { "Regular", "StaticAbs", "StaticVirt" })
        {
            var evt = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == name));
            Assert.Equal("interface", evt.ContainerKind);
            Assert.Equal("IBus", evt.ContainerName);
            Assert.Equal("EventMods.IBus", evt.ContainerQualifiedName);
        }
    }

    [Fact]
    public void Extract_CSharp_StructMembersUseStructContainer()
    {
        var content = """
            namespace Demo;

            public struct S
            {
                public int P { get; set; }
                public event System.EventHandler E;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var property = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("struct", property.ContainerKind);
        Assert.Equal("S", property.ContainerName);
        Assert.Equal("Demo.S", property.ContainerQualifiedName);

        var evt = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E"));
        Assert.Equal("struct", evt.ContainerKind);
        Assert.Equal("S", evt.ContainerName);
        Assert.Equal("Demo.S", evt.ContainerQualifiedName);
    }

    [Fact]
    public void Extract_CSharp_TypeDeclarations_FreeModifierOrder()
    {
        // Closes #355: type declarations (class / struct / interface / record) also accept
        // visibility anywhere in the modifier sequence. All fixture forms are compiler-legal
        // (`abstract public class X {}`, `readonly public struct Y {}`, `sealed public class Z {}`,
        // `ref public struct RS {}`, `partial public interface PI {}`) but previously fell
        // through the type rows because visibility had to come first.
        // Closes #355: 型宣言（class / struct / interface / record）も visibility を
        // 修飾子列の任意位置で受け付けること。fixture はすべてコンパイラが通す合法な並びで、
        // 以前は visibility が先頭必須のため型行をすり抜けていた。
        var content = """
            namespace Demo;
            abstract public class AbstractPublicClass {}
            sealed public class SealedPublicClass {}
            readonly public struct ReadonlyPublicStruct {}
            ref public struct RefPublicStruct {}
            partial public interface PartialPublicInterface {}
            abstract public record class AbstractPublicRecordClass {}
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "AbstractPublicClass" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "SealedPublicClass" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "ReadonlyPublicStruct" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "RefPublicStruct" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "PartialPublicInterface" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "AbstractPublicRecordClass" && s.Visibility == "public");
    }

    [Fact]
    public void Extract_CSharp_ConstField_FreeModifierOrder()
    {
        // Closes #355: `const` fields also accept free modifier order. `new public const`
        // is compiler-legal (it hides a same-named base-class const) but was previously
        // dropped because the const row required visibility to come before `new`.
        // Closes #355: `const` フィールドも修飾子順序自由。`new public const` は
        // コンパイラ上合法（同名ベースクラス const の隠蔽）だが、以前は visibility が
        // `new` より前必須のため落ちていた。
        var content = """
            public class Base { public const int BaseConst = 1; }
            public class Derived : Base
            {
                new public const int HiddenConst = 2;
                public new const int HiddenConst2 = 3;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "BaseConst" && s.Visibility == "public" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "HiddenConst" && s.Visibility == "public" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "HiddenConst2" && s.Visibility == "public" && s.ReturnType == "int");
    }

    [Fact]
    public void Extract_CSharp_ConstField_TupleReturnTypes()
    {
        // Closes #346: const fields with tuple / named-tuple / nullable-tuple /
        // generic-over-tuple / global::-qualified / tuple-array return types were silently
        // dropped because the const row's returnType char class had no `(`, `)`, `\s`, and no
        // tuple alternative. The method row at the next priority was already immunized by
        // the post-#349 CSharpNonTypeKeywordPattern / CSharpTypePattern consolidation, so no
        // phantom `function const` row was emitted — the symbols simply vanished. Switching
        // the const returnType to the shared CSharpTypePattern token restores capture for all
        // of these shapes and preserves baselines (`public const int Plain = 42;`,
        // `new public const int HiddenConst = 2;`).
        // Closes #346: tuple / 名前付き tuple / nullable tuple / generic-over-tuple /
        // `global::` 修飾 / tuple-array を戻り値型とする const フィールドは、const 行の
        // returnType 文字クラスに `(` / `)` / `\s` も tuple 代替もなかったため、サイレントに
        // drop されていた。method 行は #349 以後の CSharpNonTypeKeywordPattern /
        // CSharpTypePattern 統合で既にこの後方参照経路を塞いでいるため、phantom `function const`
        // 行は出ずに単に消えていた。const の returnType を共有トークン CSharpTypePattern に
        // 差し替えることで、以下のすべての形を捕捉し、既存の baseline（`public const int Plain = 42;`
        // / `new public const int HiddenConst = 2;`）も維持する。
        var content = """
            namespace ConstTuple;

            public class Cfg
            {
                public const (int, int) Pair = (1, 2);
                public const (int a, int b) NamedPair = (1, 2);
                public const (int, int)? MaybePair = null;
                public const (int, int)[] PairArray = null;
                public const global::System.Int32 Qualified = 7;
                public const int Plain = 42;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Pair" && s.Visibility == "public" && s.ReturnType == "(int, int)");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "NamedPair" && s.Visibility == "public" && s.ReturnType == "(int a, int b)");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MaybePair" && s.Visibility == "public" && s.ReturnType == "(int, int)?");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "PairArray" && s.Visibility == "public" && s.ReturnType == "(int, int)[]");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Qualified" && s.Visibility == "public" && s.ReturnType == "global::System.Int32");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Plain" && s.Visibility == "public" && s.ReturnType == "int");

        // The method row must not emit a phantom `function const` row for any of the tuple
        // shapes above. `const` itself as a name would only appear via the post-#349 backtrack
        // the issue describes. Assert the negative so any regression of that phantom is caught.
        // tuple 形に対して method 行が phantom `function const` 行を発行していないことを
        // 明示的に確認する。`const` 自体が name として現れるのは #349 以後は起きないはずの
        // 後方参照経路のみなので、将来その regression が起きたらここで検出できる。
        Assert.DoesNotContain(symbols, s => s.Name == "const");
    }

    [Fact]
    public void Extract_CSharp_PlainField_FreeModifierOrder()
    {
        // Closes #355: plain fields (kind `property`) and multi-line field headers must also
        // accept visibility anywhere in the modifier sequence. Previously `static public int X;`
        // captured as a field with empty `visibility` (single-line plain-field regex was
        // visibility-first), and multi-line declarations whose header line starts with a
        // non-visibility modifier were dropped entirely because
        // `CSharpPropertyHeaderPrefixRegex` (the merger trigger) was also visibility-first and
        // did not accept `const`.
        // Closes #355: 通常フィールド（kind `property`）と複数行フィールドヘッダも、修飾子列の
        // 任意位置で visibility を受け付けなければならない。以前は `static public int X;` が
        // visibility 空のまま captured され（単一行 plain-field 正規表現が visibility-first）、
        // 非 visibility 修飾子から始まる複数行宣言は結合トリガの `CSharpPropertyHeaderPrefixRegex`
        // 自体が visibility-first で `const` も受け付けなかったため完全に欠落していた。
        var content = """
            using System.Collections.Generic;
            public class Edge
            {
                static public int X;
                readonly public int Y;
                new public static int Z = 1;
                static public Dictionary<string, int>
                    Map = new();
                new public const int
                    C = 1;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "X" && s.Visibility == "public" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Y" && s.Visibility == "public" && s.ReturnType == "int");
        // `new public static` is promoted to kind `function` via the static readonly / const row set.
        // `new public static` は static readonly / const 系の行で kind `function` に昇格する。
        Assert.Contains(symbols, s => s.Name == "Z" && s.Visibility == "public" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Map" && s.Visibility == "public" && s.ReturnType != null && s.ReturnType.Contains("Dictionary"));
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "C" && s.Visibility == "public" && s.ReturnType == "int");
    }

    [Fact]
    public void Extract_CSharp_UnsafeExtern_FreeModifierOrder()
    {
        // Closes #355: `unsafe` / `extern` modifiers must not force a specific slot in the
        // modifier sequence. All fixture forms are compiler-legal but previously either dropped
        // entirely (constructor / static constructor / event) or captured the declaration while
        // losing `visibility` and polluting `return_type` with the leading modifiers
        // (property / indexer).
        // Closes #355: `unsafe` / `extern` 修飾子も修飾子列の特定位置に固定されてはならない。
        // fixture はすべてコンパイラ上合法だが、以前は constructor / static constructor / event では
        // そもそも抽出されず、property / indexer では visibility が欠落して return_type に
        // 先頭修飾子が混入していた。
        var content = """
            public unsafe class UnsafeHolder
            {
                unsafe public int P1 { get; set; }
                unsafe public int P2 => 0;
                unsafe public event System.EventHandler E1;
                extern public event System.EventHandler E2;
                unsafe public int this[int* i] => 0;
                unsafe public UnsafeHolder(int* p) { }
                extern public UnsafeHolder(int x);
                unsafe static UnsafeHolder() { }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "P1" && s.Visibility == "public" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "P2" && s.Visibility == "public" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "E1" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "E2" && s.Visibility == "public");
        // Indexer is recorded as `Item` (C# metadata name) after NormalizeCSharpSymbolName.
        // インデクサは NormalizeCSharpSymbolName で C# メタデータ名 `Item` に正規化される。
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Item" && s.Visibility == "public" && s.ReturnType == "int");
        // Constructors are recorded with visibility and the type name as symbol name.
        // コンストラクタは visibility を保持し、シンボル名は型名になる。
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "UnsafeHolder" && s.Visibility == "public");
        // Static constructor has no visibility.
        // 静的コンストラクタは visibility を持たない。
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "UnsafeHolder" && string.IsNullOrEmpty(s.Visibility));
    }

    [Fact]
    public void Extract_CSharp_InheritanceAndFile_FreeModifierOrder()
    {
        // Closes #355: inheritance modifiers on events (`virtual` / `override` / `abstract` /
        // `sealed` / `new`) and the `file` modifier on interface / delegate declarations must
        // be accepted in any position, with visibility still captured when present.
        // Closes #355: event の継承修飾子 (`virtual` / `override` / `abstract` / `sealed` / `new`) と、
        // interface / delegate 宣言の `file` 修飾子は任意位置で受理され、visibility が存在する場合は
        // 併せて拾われる必要がある。
        var content = """
            file interface IWidget
            {
                int ProvideAnswer();
            }
            file delegate int Computer(int x);
            public abstract class Base
            {
                abstract public event System.EventHandler A;
                virtual public event System.EventHandler B;
                sealed public override event System.EventHandler C;
                new public event System.EventHandler D;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        // `file interface` should be matched as an interface symbol.
        // `file interface` は interface シンボルとして抽出される。
        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "IWidget");
        // `file delegate` should be matched as a delegate symbol.
        // `file delegate` は delegate シンボルとして抽出される。
        Assert.Contains(symbols, s => s.Kind == "delegate" && s.Name == "Computer" && s.ReturnType == "int");
        // Events with inheritance modifiers must still record visibility = "public".
        // 継承修飾子付きの event も visibility = "public" を保持する必要がある。
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "A" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "B" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "C" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "D" && s.Visibility == "public");
    }

    [Fact]
    public void Extract_CSharp_FileDelegate_Issue303Repro()
    {
        // Closes #303: the `file` modifier on a top-level delegate must not be dropped by
        // the modifier slot, regardless of whether other variants share the same file.
        // v1.10.0 captured plain / public / unsafe delegates but silently missed
        // `file delegate`. The #355 fix (free modifier order; accept `file` / `new`) covers
        // this, so this test locks in the behavior against the exact reproducer from #303.
        // Closes #303: トップレベル delegate の `file` 修飾子が modifier スロットで落ちないこと。
        // v1.10.0 では plain / public / unsafe delegate は拾えていたが `file delegate` だけが
        // 黙って欠落していた。#355 の修正 (修飾子順序を自由化し `file` / `new` を受理) が
        // 本件も解消するため、#303 の再現 fixture で挙動を固定する。
        var content = """
            namespace Demo;

            public delegate void PublicHandler(object sender);

            file delegate void FileOnlyHandler(object sender);

            delegate void PlainHandler(object sender);

            unsafe delegate void UnsafeHandler(object sender);
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "delegate" && s.Name == "PublicHandler" && s.Visibility == "public" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "delegate" && s.Name == "FileOnlyHandler" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "delegate" && s.Name == "PlainHandler" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "delegate" && s.Name == "UnsafeHandler" && s.ReturnType == "void");
    }

    [Fact]
    public void Extract_CSharp_EventModifierCombinations_Issue334Repro()
    {
        // Closes #334: the full issue repro must survive extraction, including class events
        // with `abstract` / `virtual` / `override` / `sealed override` / `new`, plus interface
        // events with `static abstract` and accessor-bodied `static virtual`. The current main
        // branch already accepts these modifier sequences; this test locks the exact dogfood
        // fixture in place so the open issue cannot regress silently. The same container
        // walk now also treats `struct` as a real parent, so keep one struct-owned event in
        // the fixture to ensure the broader container fix stays covered too.
        // Closes #334: issue 本文の再現ケース全体を固定する。`abstract` / `virtual` /
        // `override` / `sealed override` / `new` 付き class event と、`static abstract` /
        // accessor 本体付き `static virtual` interface event の両方が抽出され続ける必要がある。
        // 現行 main はこれらを受理できるため、このテストは open issue の dogfood fixture を
        // そのまま回帰防止として固定する。同じ container 走査は `struct` も親として扱うよう
        // になったため、より広い親子付け修正も 1 件の struct event で固定する。
        var content = """
            using System;
            namespace EventMods;

            public abstract class Base
            {
                public abstract event EventHandler Ping;
                public virtual event EventHandler Ring;
                public new event EventHandler Hide;
                protected event EventHandler Peek;
                public event EventHandler Plain;
            }

            public sealed class Derived : Base
            {
                public override event EventHandler Ping;
                public sealed override event EventHandler Ring;
            }

            public struct Box
            {
                public event EventHandler Sent;
            }

            public interface IBus
            {
                event EventHandler Regular;
                static abstract event EventHandler StaticAbs;
                static virtual event EventHandler StaticVirt { add { } remove { } }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var events = symbols.Where(s => s.Kind == "event").ToList();

        Assert.Equal(11, events.Count);
        var basePing = Assert.Single(events.Where(s => s.Name == "Ping" && s.ContainerKind == "class" && s.ContainerName == "Base"));
        Assert.Equal("public", basePing.Visibility);
        Assert.Equal("EventHandler", basePing.ReturnType);

        var derivedPing = Assert.Single(events.Where(s => s.Name == "Ping" && s.ContainerKind == "class" && s.ContainerName == "Derived"));
        Assert.Equal("public", derivedPing.Visibility);
        Assert.Equal("EventHandler", derivedPing.ReturnType);

        var baseRing = Assert.Single(events.Where(s => s.Name == "Ring" && s.ContainerKind == "class" && s.ContainerName == "Base"));
        Assert.Equal("public", baseRing.Visibility);
        Assert.Equal("EventHandler", baseRing.ReturnType);

        var derivedRing = Assert.Single(events.Where(s => s.Name == "Ring" && s.ContainerKind == "class" && s.ContainerName == "Derived"));
        Assert.Equal("public", derivedRing.Visibility);
        Assert.Equal("EventHandler", derivedRing.ReturnType);

        Assert.Contains(events, s => s.Name == "Hide" && s.ContainerKind == "class" && s.ContainerName == "Base" && s.Visibility == "public" && s.ReturnType == "EventHandler");
        Assert.Contains(events, s => s.Name == "Peek" && s.ContainerKind == "class" && s.ContainerName == "Base" && s.Visibility == "protected" && s.ReturnType == "EventHandler");
        Assert.Contains(events, s => s.Name == "Plain" && s.ContainerKind == "class" && s.ContainerName == "Base" && s.Visibility == "public" && s.ReturnType == "EventHandler");
        var sent = Assert.Single(events.Where(s => s.Name == "Sent" && s.ContainerKind == "struct" && s.ContainerName == "Box"));
        Assert.Equal("public", sent.Visibility);
        Assert.Equal("EventHandler", sent.ReturnType);

        var regular = Assert.Single(events.Where(s => s.Name == "Regular" && s.ContainerKind == "interface" && s.ContainerName == "IBus"));
        Assert.True(string.IsNullOrEmpty(regular.Visibility));
        Assert.Equal("EventHandler", regular.ReturnType);

        var staticAbs = Assert.Single(events.Where(s => s.Name == "StaticAbs" && s.ContainerKind == "interface" && s.ContainerName == "IBus"));
        Assert.True(string.IsNullOrEmpty(staticAbs.Visibility));
        Assert.Equal("EventHandler", staticAbs.ReturnType);

        var staticVirt = Assert.Single(events.Where(s => s.Name == "StaticVirt" && s.ContainerKind == "interface" && s.ContainerName == "IBus"));
        Assert.True(string.IsNullOrEmpty(staticVirt.Visibility));
        Assert.Equal("EventHandler", staticVirt.ReturnType);
    }

    [Fact]
    public void Extract_CSharp_NewNestedInterface_MemberHiding()
    {
        // Closes #376: a nested `new interface` that hides a base-class nested interface must
        // still be extracted as its own symbol. Previously the interface regex modifier list
        // did not include `new`, so `public new interface INested` was silently dropped.
        // Closes #376: ベースクラスのネストしたインタフェースを `new interface` で隠蔽する
        // 派生側のネスト interface も独立したシンボルとして抽出されること。以前は
        // interface 正規表現の修飾子リストに `new` が無く、`public new interface INested`
        // が無言で落ちていた。
        var content = """
            namespace NewIfaceTest;
            public class Base
            {
                public interface INested { void M(); }
            }
            public class Derived : Base
            {
                public new interface INested { void M(); }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var nested = symbols.Where(s => s.Kind == "interface" && s.Name == "INested").ToList();
        Assert.Equal(2, nested.Count);
        Assert.Contains(nested, s => s.ContainerName == "Base");
        Assert.Contains(nested, s => s.ContainerName == "Derived");
    }

    [Fact]
    public void Extract_CSharp_Interface_ModifierSlotMatrix_LocksInCommonLegalShapes()
    {
        // Closes #302: the C# interface row's modifier slot must accept the common
        // legal declaration shapes in a single fixture so a future modifier-slot
        // refactor (mirror of the #238 `operator checked`, #244 `static abstract`,
        // #355 `file`, and #376 `new` families) cannot silently drop one variant.
        // The fixture is intentionally hand-verified to be legal C# — `partial`
        // must appear immediately before the `interface` keyword (CS0267 otherwise)
        // so `partial public interface` is NOT a legal ordering and is intentionally
        // absent. Non-canonical modifier order is instead demonstrated by
        // `unsafe public interface` (the `unsafe` type modifier has no required
        // position relative to accessibility). Covers plain `interface`, `public
        // interface`, explicit `internal interface`, `file interface` (C# 11
        // file-scoped, cannot combine with accessibility), bare `partial interface`,
        // `public partial interface`, non-canonical `unsafe public interface`,
        // `unsafe interface`, the nested `public new interface` that hides a
        // same-named base-type member, and the nested `public new partial interface`
        // that exercises the `new + partial` modifier interaction on nested types.
        // Each unique name is pinned with `Assert.Single` so a silent duplicate row
        // or a kind/visibility relabel on a sibling variant cannot make this test
        // pass via a second matching row. The total interface-symbol count for the
        // fixture is also asserted so a phantom extra interface emission anywhere
        // in the file cannot slip past the per-name predicates.
        // Closes #302: C# interface 行の修飾子スロットが、単一 fixture で代表的な
        // 合法宣言形を受理することを固定する。修飾子スロットの将来的な再編（#238
        // の `operator checked`、#244 の `static abstract`、#355 の `file`、#376
        // の `new` と同じファミリの問題）で、いずれか1形を黙って落とす回帰を防ぐ。
        // fixture は手で合法性を検証済みで、`partial` は `interface` キーワード
        // 直前にしか置けず（違反すると CS0267）、`partial public interface` は
        // 合法な順序ではないので意図的に含めない。非正準順序は `unsafe public
        // interface`（`unsafe` 型修飾子は可視性に対して順序の制約がない）で代替
        // する。plain `interface`、`public interface`、明示 `internal interface`、
        // `file interface`（C# 11 file-scoped、accessibility と併用不可）、素の
        // `partial interface`、`public partial interface`、非正準順の `unsafe
        // public interface`、`unsafe interface`、同名ベースメンバを隠蔽する
        // ネストの `public new interface`、`new + partial` 修飾子相互作用を
        // 検証するネストの `public new partial interface` を網羅する。各ユニーク
        // 名は `Assert.Single` で固定し、兄弟変種に silent duplicate や kind /
        // visibility relabel が入っても別行のヒットで silent pass しないように
        // する。fixture 全体の interface シンボル総数もアサートして、ファイル内
        // のどこかで phantom interface が追加された場合でも per-name predicate
        // をすり抜けないようにする。
        var content = """
            namespace ModifierSlotMatrix;

            interface IPlain { void Do(); }
            public interface IPublic { void Do(); }
            internal interface IInternal { void Do(); }
            file interface IFile { void Do(); }
            partial interface IPartial { void Do(); }
            public partial interface IPublicPartial { void Do(); }
            unsafe public interface IUnsafePublic { void Do(); }
            unsafe interface IUnsafe { void Do(); }

            public class Base
            {
                public interface INested { void Do(); }
            }
            public class Derived : Base
            {
                public new interface INested { void Do(); }
                public new partial interface IPartialNested { void Do(); }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Single(symbols, s => s.Kind == "interface" && s.Name == "IPlain" && string.IsNullOrEmpty(s.Visibility));
        Assert.Single(symbols, s => s.Kind == "interface" && s.Name == "IPublic" && s.Visibility == "public");
        Assert.Single(symbols, s => s.Kind == "interface" && s.Name == "IInternal" && s.Visibility == "internal");
        Assert.Single(symbols, s => s.Kind == "interface" && s.Name == "IFile" && string.IsNullOrEmpty(s.Visibility));
        Assert.Single(symbols, s => s.Kind == "interface" && s.Name == "IPartial" && string.IsNullOrEmpty(s.Visibility));
        Assert.Single(symbols, s => s.Kind == "interface" && s.Name == "IPublicPartial" && s.Visibility == "public");
        Assert.Single(symbols, s => s.Kind == "interface" && s.Name == "IUnsafePublic" && s.Visibility == "public");
        Assert.Single(symbols, s => s.Kind == "interface" && s.Name == "IUnsafe" && string.IsNullOrEmpty(s.Visibility));

        // Nested `public new interface INested` must produce a second symbol attributed
        // to the `Derived` container alongside the base-side `INested` on `Base`.
        // `public new partial interface IPartialNested` covers the `new + partial`
        // modifier interaction on nested types.
        // ネストの `public new interface INested` は、基底側の `Base.INested` に加えて
        // `Derived` コンテナ下の独立シンボルとして抽出される必要がある。
        // `public new partial interface IPartialNested` はネスト型の `new + partial`
        // 修飾子相互作用を検証する。
        var nested = symbols.Where(s => s.Kind == "interface" && s.Name == "INested").ToList();
        Assert.Equal(2, nested.Count);
        Assert.Single(nested, s => s.ContainerName == "Base" && s.Visibility == "public");
        Assert.Single(nested, s => s.ContainerName == "Derived" && s.Visibility == "public");
        Assert.Single(symbols, s => s.Kind == "interface" && s.Name == "IPartialNested" && s.ContainerName == "Derived" && s.Visibility == "public");

        // Fixture contains exactly 11 legal interface declarations (8 top-level +
        // 3 nested: Base.INested, Derived.INested, Derived.IPartialNested). Pinning
        // the aggregate count here prevents a phantom interface emission elsewhere
        // in the file from slipping past the per-name `Assert.Single` predicates.
        // fixture 全体の合法 interface 宣言は正確に 11 件（top-level 8 件 + nested 3
        // 件: Base.INested、Derived.INested、Derived.IPartialNested）。集計数も
        // アサートすることで、ファイル中のどこかで phantom interface が発生しても
        // per-name `Assert.Single` をすり抜けないようにする。
        Assert.Equal(11, symbols.Count(s => s.Kind == "interface"));
    }

    [Fact]
    public void Extract_CSharp_DetectsExpressionBodiedMembers()
    {
        var content = "public class Calc\n{\n    public int X => 42;\n    public string Name => \"calc\";\n    public static double Pi => 3.14;\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "X" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Name" && s.ReturnType == "string");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Pi" && s.ReturnType == "double");
    }

    [Fact]
    public void Extract_CSharp_ExpressionBodiedMembers_HaveBodyRanges()
    {
        // issue #233: expression-bodied members must report a body range covering the
        // declaration line through the terminating ';' so reference attribution can find
        // them as the innermost enclosing container.
        // issue #233: 式本体メンバーは、宣言行から終端 ';' までを本体範囲として報告する必要がある。
        // そうすることで参照属性解決が内側コンテナとして認識できる。
        var content = "public class Calc\n{\n    public int Compute() => 42;\n    public int Wrap1() => Compute();\n    public int Wrap3 => Compute();\n    public int MultiLine()\n        => Compute();\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var compute = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Compute"));
        Assert.Equal(3, compute.BodyStartLine);
        Assert.Equal(3, compute.BodyEndLine);

        var wrap1 = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Wrap1"));
        Assert.Equal(4, wrap1.BodyStartLine);
        Assert.Equal(4, wrap1.BodyEndLine);

        var wrap3 = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Wrap3"));
        Assert.Equal(5, wrap3.BodyStartLine);
        Assert.Equal(5, wrap3.BodyEndLine);

        var multi = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "MultiLine"));
        Assert.Equal(6, multi.BodyStartLine);
        Assert.Equal(7, multi.BodyEndLine);
    }

    [Fact]
    public void Extract_CSharp_BlockBodiedProperty_AllmanStyle_IsExtracted()
    {
        // issue #233 review follow-up: Allman-style block-bodied properties (with `{` on
        // the next line) were not matched by the property regex, so `callers` would
        // attribute accessor-internal references to the enclosing class. The widened
        // regex plus `ShouldSkipCSharpHeaderOnlyPropertyCandidate` verification must
        // still recognize them as properties with proper body ranges.
        // issue #233 のレビュー指摘: Allman スタイル（次行に `{`）の block-bodied property が
        // property regex でマッチしておらず、accessor 内の参照がクラスに帰属していた。
        // widened regex と `ShouldSkipCSharpHeaderOnlyPropertyCandidate` の組み合わせで
        // 正しく property として認識され、本体範囲も持つ必要がある。
        var content = "public class Calc\n{\n    public int Compute() => 42;\n    public int Wrap\n    {\n        get { return Compute(); }\n    }\n    public string Name\n    {\n        get;\n        set;\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var wrap = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Wrap"));
        Assert.Equal(4, wrap.StartLine);
        Assert.Equal(7, wrap.EndLine);
        Assert.Equal(5, wrap.BodyStartLine);
        Assert.Equal(7, wrap.BodyEndLine);
        Assert.Equal("int", wrap.ReturnType);
        Assert.Equal("public", wrap.Visibility);

        var name = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Name"));
        Assert.Equal(8, name.StartLine);
        Assert.Equal(12, name.EndLine);
        Assert.Equal(9, name.BodyStartLine);
        Assert.Equal(12, name.BodyEndLine);
        Assert.Equal("string", name.ReturnType);
    }

    [Fact]
    public void Extract_CSharp_MultiLineExpressionBodiedProperty_IsExtracted()
    {
        // issue #233 second review follow-up: multi-line expression-bodied properties,
        // where the declaration is on one line and `=> expr;` on the continuation line,
        // must still be extracted as properties with a body range spanning the two lines.
        // Without this, accessor-internal calls fall through to the enclosing class.
        // issue #233 の再レビュー指摘: 宣言行の次行に `=> expr;` が来る multi-line 式本体
        // プロパティも property として抽出され、本体範囲が宣言行から `;` 行までを覆う必要がある。
        // これができないと accessor 内呼び出しが外側クラスに誤帰属する。
        var content = "public class Calc\n{\n    public int Compute() => 42;\n    public int Wrap\n        => Compute();\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var wrap = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Wrap"));
        Assert.Equal(4, wrap.StartLine);
        Assert.Equal(5, wrap.EndLine);
        Assert.Equal(4, wrap.BodyStartLine);
        Assert.Equal(5, wrap.BodyEndLine);
        Assert.Equal("int", wrap.ReturnType);
        Assert.Equal("public", wrap.Visibility);
    }

    [Fact]
    public void Extract_CSharp_WrappedExpressionBodiedProperty_Issue345Repro_ShapesAndControls_AreCaptured()
    {
        // issue #345: explicit regression coverage for expression-bodied properties whose
        // `=>` moves to the next physical line, including attributed/static variants and
        // a multi-line expression body. Indexers and methods with wrapped `=>` remain the
        // expected control cases and must not be reclassified as properties.
        // issue #345: `=>` が次の物理行へ送られた式本体プロパティの明示的な回帰テスト。
        // attribute/static 付きや multi-line 式本体も含めて property として抽出しつつ、
        // wrapped `=>` の indexer / method は従来どおり control case として残す。
        var content = """
            namespace WrappedArrowProp;

            public class Svc
            {
                public int Same => 1;

                public int Wrapped
                    => 2;

                [System.Obsolete]
                public int WrappedAttr
                    => 3;

                public static int WrappedStatic
                    => 4;

                public int WrappedMulti
                    => 1
                     + 2;

                public int this[int i]
                    => i;

                public int WrappedMethod()
                    => 5;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Same");

        var wrapped = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Wrapped"));
        Assert.Equal(7, wrapped.StartLine);
        Assert.Equal(8, wrapped.EndLine);
        Assert.Equal(7, wrapped.BodyStartLine);
        Assert.Equal(8, wrapped.BodyEndLine);

        var wrappedAttr = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "WrappedAttr"));
        Assert.Equal(11, wrappedAttr.StartLine);
        Assert.Equal(12, wrappedAttr.EndLine);
        Assert.Equal(11, wrappedAttr.BodyStartLine);
        Assert.Equal(12, wrappedAttr.BodyEndLine);

        var wrappedStatic = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "WrappedStatic"));
        Assert.Equal(14, wrappedStatic.StartLine);
        Assert.Equal(15, wrappedStatic.EndLine);
        Assert.Equal(14, wrappedStatic.BodyStartLine);
        Assert.Equal(15, wrappedStatic.BodyEndLine);
        Assert.Equal("public", wrappedStatic.Visibility);

        var wrappedMulti = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "WrappedMulti"));
        Assert.Equal(17, wrappedMulti.StartLine);
        Assert.Equal(19, wrappedMulti.EndLine);
        Assert.Equal(17, wrappedMulti.BodyStartLine);
        Assert.Equal(19, wrappedMulti.BodyEndLine);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Item");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "WrappedMethod");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "Item");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "WrappedMethod");
    }

    [Fact]
    public void Extract_CSharp_SplitReturnTypeLine_StillCapturesMethodPropertyAndIndexer()
    {
        // issue #361: when a long C# return type wraps onto the previous line, the
        // method/property/indexer must still be emitted instead of being silently
        // dropped by a per-line-only regex pass.
        // issue #361: C# の長い戻り値型が前行へ折り返されても、method/property/indexer は
        // per-line 前提の regex で silent drop されず、引き続き抽出される必要がある。
        var content = """
            using System.Collections.Generic;

            namespace CsMultilineSig;

            public class Svc
            {
                public Dictionary<string, List<int>>
                    GetMapping(
                        string key,
                        int defaultValue) => new();

                public Dictionary<string, int>
                    Cache { get; } = new();

                public T Create<T>(string name)
                    where T : class, new()
                    => default!;

                public int
                    this[string key] { get => 0; set { } }

                public int Simple() => 0;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var getMapping = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "GetMapping"));
        Assert.Equal("public", getMapping.Visibility);
        Assert.Equal("Dictionary<string,List<int>>", getMapping.ReturnType);
        Assert.Equal(7, getMapping.StartLine);
        Assert.Equal(10, getMapping.EndLine);

        var cache = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Cache"));
        Assert.Equal("public", cache.Visibility);
        Assert.Equal("Dictionary<string,int>", cache.ReturnType);
        Assert.Equal(12, cache.StartLine);
        Assert.Equal(13, cache.EndLine);

        var create = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Create"));
        Assert.Equal("public", create.Visibility);
        Assert.Equal("T", create.ReturnType);
        Assert.Equal(15, create.StartLine);
        Assert.Equal(17, create.EndLine);

        var indexer = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Item"));
        Assert.Equal("public", indexer.Visibility);
        Assert.Equal("int", indexer.ReturnType);
        Assert.Equal(19, indexer.StartLine);
        Assert.Equal(20, indexer.EndLine);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Simple" && s.ReturnType == "int");
        Assert.Equal(
            new[]
            {
                ("class", "Svc"),
                ("function", "Create"),
                ("function", "GetMapping"),
                ("function", "Item"),
                ("function", "Simple"),
                ("namespace", "CsMultilineSig"),
                ("property", "Cache"),
            },
            symbols
                .Where(s => s.Kind != "import")
                .Select(s => (s.Kind, s.Name))
                .OrderBy(x => x.Kind, StringComparer.Ordinal)
                .ThenBy(x => x.Name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void Extract_CSharp_AllmanBlockBodiedProperty_WithIntermediateBlockComment_IsExtracted()
    {
        // issue #233 fourth review follow-up: when an Allman-style block-bodied property
        // has a multi-line `/* ... */` block comment between the header line and the `{`
        // line, the skip guard must traverse the comment via `LexCSharpLine` and still
        // recognize the continuation `{`. A naive prefix-based comment skip only
        // handled `*` / `//` / `/*` line starts and dropped the property entirely.
        // issue #233 第4次レビュー指摘: Allman スタイルの block-bodied property で
        // header 行と `{` の間に multi-line `/* ... */` のブロックコメントがあっても、
        // `LexCSharpLine` でコメントを通り抜けて次の `{` を認識する必要がある。
        // 行頭 prefix だけの素朴なスキップでは `*` / `//` / `/*` の開始行しか飛ばせず、
        // この形の property は落ちていた。
        var content = "public class Calc\n{\n    public int Compute() => 42;\n    public int Wrap\n    /* some multi-line\n       block comment */\n    {\n        get { return Compute(); }\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var wrap = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Wrap"));
        Assert.Equal(4, wrap.StartLine);
        Assert.Equal(9, wrap.EndLine);
        Assert.Equal(7, wrap.BodyStartLine);
        Assert.Equal(9, wrap.BodyEndLine);
        Assert.Equal("int", wrap.ReturnType);
    }

    [Fact]
    public void Extract_CSharp_MultiLineExpressionBodiedProperty_WithIntermediateBlockComment_IsExtracted()
    {
        // issue #233 fourth review follow-up: same scenario for multi-line expression
        // bodies — `public int Wrap` followed by `/* ... */` and then `=> Compute();`
        // must still be extracted with the property spanning declaration through `;`.
        // issue #233 第4次レビュー指摘: multi-line 式本体プロパティでも同じく、
        // `public int Wrap` の後に `/* ... */`、さらに `=> Compute();` が続く形で
        // 宣言行から `;` 行までを本体範囲とする property が抽出されること。
        var content = "public class Calc\n{\n    public int Compute() => 42;\n    public int WrapExpr\n    /* multi-line\n       comment */\n        => Compute();\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var wrap = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "WrapExpr"));
        Assert.Equal(4, wrap.StartLine);
        Assert.Equal(7, wrap.EndLine);
        Assert.Equal(4, wrap.BodyStartLine);
        Assert.Equal(7, wrap.BodyEndLine);
        Assert.Equal("int", wrap.ReturnType);
    }

    [Fact]
    public void Extract_CSharp_BraceSameLineAccessorNextLineProperty_IsExtracted()
    {
        // issue #233 fifth review follow-up: the common Microsoft-style block-bodied
        // property — `{` on the same line as the declaration and the accessor on the
        // following line — must be recognized as a property with a body range spanning
        // declaration through closing `}`.
        // issue #233 第5次レビュー指摘: `{` が宣言行末にあり、accessor が次行にある
        // 標準的な block-bodied property が property として抽出され、宣言行から `}` 行
        // までを本体範囲として持つこと。
        var content = "public class Calc\n{\n    public int Compute() => 42;\n    public int Wrap {\n        get { return Compute(); }\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var wrap = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Wrap"));
        Assert.Equal(4, wrap.StartLine);
        Assert.Equal(6, wrap.EndLine);
        Assert.Equal(4, wrap.BodyStartLine);
        Assert.Equal(6, wrap.BodyEndLine);
        Assert.Equal("int", wrap.ReturnType);
    }

    [Fact]
    public void Extract_CSharp_BraceSameLineAccessorNextLine_AcceptsAttributeAndVisibility()
    {
        // issue #233 fifth review follow-up: the bare-brace-same-line guard must also
        // accept next lines that begin with accessor attributes (`[JsonIgnore]`) or a
        // visibility modifier (`private set`) before the `get` / `set` / `init` token.
        // issue #233 第5次レビュー指摘: 同一行 bare `{` のガードは、accessor attribute
        // (`[JsonIgnore]`) や visibility 修飾子 (`private set`) で始まる行も受け入れる必要がある。
        var content = "public class Calc\n{\n    public int Compute() => 42;\n"
            + "    public int WithAttr {\n        [System.Obsolete] get => Compute();\n    }\n"
            + "    public int WithVis {\n        private set { }\n        get { }\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "WithAttr");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "WithVis");
    }

    [Fact]
    public void Extract_CSharp_BraceSameLineWithoutAccessor_IsNotMisclassifiedAsProperty()
    {
        // issue #233 fifth review follow-up: the bare-brace-same-line guard must reject
        // non-property shapes that happen to be `Type Name {` followed by a body that
        // does not start an accessor (for example a stray method-like block).
        // issue #233 第5次レビュー指摘: 同一行 bare `{` のガードは、`Type Name {` に
        // 続く行が accessor 宣言でない場合（例: accessor でない任意のブロック）を
        // property として採用してはならない。
        var content = "public class Calc\n{\n    public int Stray {\n        Console.WriteLine(1);\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "Stray");
    }

    [Fact]
    public void Extract_CSharp_HeaderOnlyNonProperty_IsNotMisclassified()
    {
        // issue #233 review follow-up: the header-only property alternation must not
        // swallow keyword lines such as `public class X` or `return Foo` even if they
        // happen to look like `Type Name` before a newline.
        // issue #233 のレビュー指摘: header-only の alternation が `public class X` や
        // `return Foo` のようなキーワード行を property と誤分類しないことを担保する。
        var content = "public class Thing\n{\n    public int Method()\n    {\n        return Thing;\n    }\n}\n";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "property");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Thing");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Method");
    }

    [Fact]
    public void Extract_CSharp_SwitchExpressionArms_DoNotProducePhantomProperties()
    {
        var content = """
            public class Matcher
            {
                public string Describe(object x) => x switch
                {
                    int n when n > 0 => "pos",
                    int neg => "non-pos",
                    string text => text,
                    double d => "double",
                    List<string> list => "list",
                    _ => "other",
                };

                public int Count(int y) => y switch
                {
                    > 0 => 1,
                    0 => 0,
                    _ => -1,
                };
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Matcher");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Describe");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Count");
        Assert.DoesNotContain(symbols, s => s.Kind == "property");
        Assert.DoesNotContain(symbols, s => s.Name == "neg");
        Assert.DoesNotContain(symbols, s => s.Name == "text");
        Assert.DoesNotContain(symbols, s => s.Name == "d");
        Assert.DoesNotContain(symbols, s => s.Name == "list");
        Assert.DoesNotContain(symbols, s => s.Name == "0");
    }

    [Fact]
    public void Extract_CSharp_MultiLineSwitchExpressionArms_DoNotProducePhantomProperties()
    {
        // issue #233 third review follow-up: switch-expression arms whose `=>` is placed
        // on a continuation line must not be misclassified as multi-line expression-bodied
        // properties. Without switch-expression guard coverage on the continuation `=>`,
        // each pattern variable (e.g. `text`, `neg`) would be emitted as a phantom property
        // and `callers` / `impact` would misattribute calls inside the arm to it.
        // issue #233 第3次レビュー指摘: switch expression arm の `=>` が継続行にある
        // multi-line 形を、multi-line 式本体プロパティと誤認しないこと。continuation `=>`
        // まで switch-expression ガードが及ばないと、`text` や `neg` のようなパターン変数が
        // phantom property として抽出され、arm 内の呼び出しが phantom に誤帰属する。
        var content = """
            public class Matcher
            {
                public string Describe(object x) => x switch
                {
                    string text
                        => text.Trim(),
                    int neg
                        => "non-pos",
                    _
                        => "other",
                };
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Matcher");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Describe");
        Assert.DoesNotContain(symbols, s => s.Kind == "property");
        Assert.DoesNotContain(symbols, s => s.Name == "text");
        Assert.DoesNotContain(symbols, s => s.Name == "neg");
    }

    [Fact]
    public void Extract_CSharp_DetectsExplicitInterfaceImpl()
    {
        // Issue #333: the qualifier-pattern widening that unblocked explicit-interface
        // property extraction also fixes the pre-existing method row for multi-argument
        // generic qualifiers (e.g. `IMap<string, int>.GetCount`) and qualifiers that embed
        // nullable / array type arguments.
        // Issue #333: explicit-interface プロパティ抽出のために広げた qualifier パターンは、
        // 既存のメソッド行にも波及し、`IMap<string, int>.GetCount` のような多引数 generic
        // 修飾子や、nullable / array を含む型引数を正しく拾えるようになる。
        var content = "public class MyClass : IDisposable, IComparable<MyClass>\n{\n    void IDisposable.Dispose()\n    {\n    }\n    int IComparable<MyClass>.CompareTo(MyClass other) => 0;\n    int IMap<string, int>.GetCount() => 0;\n    string IFoo<string?>.NullableArg() => \"n\";\n    string IFoo<int[]>.ArrayArg() => \"a\";\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Dispose" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "CompareTo" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetCount" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "NullableArg" && s.ReturnType == "string");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ArrayArg" && s.ReturnType == "string");
    }

    [Fact]
    public void Extract_CSharp_DetectsGenericOverTupleReturnTypes()
    {
        // Issue #241 / #344 / #484: the shared C# return-type matcher must allow tuple groups
        // inside generic arguments so ordinary methods, interface declarations, and
        // explicit-interface implementations do not silently disappear, even when tuple
        // elements themselves contain nested tuples.
        // Issue #241 / #344 / #484: 共有の C# 戻り値型 matcher は generic 引数内の tuple を
        // 許容し、通常メソッド・interface 宣言・明示的インターフェース実装が
        // 無言で消えないようにしなければならず、tuple 要素側の入れ子 tuple も扱えなければならない。
        var content = """
            namespace Demo;

            public interface IFoo
            {
                System.Collections.Generic.List<(int, int)> GetList();
                System.Threading.Tasks.Task<((int A, int B), string Name)> Nested();
                System.Threading.Tasks.Task<(((int A, int B), int C), string Name)> TooDeep();
            }

            public class Service : IFoo
            {
                public System.Threading.Tasks.Task<(int, string)> MultiAsync() => System.Threading.Tasks.Task.FromResult((1, "x"));
                public System.Collections.Generic.Dictionary<string, (int x, int y)> Coords() => new();
                public System.Collections.Generic.Dictionary<string, (int x, int y)> CoordsProperty { get; } = new();
                public System.Collections.Generic.IEnumerable<(string Key, int Value)> Items() => [];
                System.Collections.Generic.List<(int, int)> IFoo.GetList() => [];
                public System.Threading.Tasks.Task<((int A, int B), string Name)> NestedAsync() => System.Threading.Tasks.Task.FromResult(((1, 2), "n"));
                public System.Threading.Tasks.Task<(((int A, int B), int C), string Name)> TooDeepAsync() => System.Threading.Tasks.Task.FromResult((((1, 2), 3), "deep"));
                System.Threading.Tasks.Task<((int A, int B), string Name)> IFoo.Nested() => System.Threading.Tasks.Task.FromResult(((1, 2), "n"));
                System.Threading.Tasks.Task<(((int A, int B), int C), string Name)> IFoo.TooDeep() => System.Threading.Tasks.Task.FromResult((((1, 2), 3), "deep"));
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var multiAsync = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "MultiAsync"));
        Assert.Equal("System.Threading.Tasks.Task<(int,string)>", multiAsync.ReturnType);

        var coords = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Coords"));
        Assert.Equal("System.Collections.Generic.Dictionary<string,(int x,int y)>", coords.ReturnType);

        var coordsProperty = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "CoordsProperty"));
        Assert.Equal("System.Collections.Generic.Dictionary<string,(int x,int y)>", coordsProperty.ReturnType);

        var items = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Items"));
        Assert.Equal("System.Collections.Generic.IEnumerable<(string Key,int Value)>", items.ReturnType);

        var getListDeclarations = symbols.Where(s => s.Kind == "function" && s.Name == "GetList").ToList();
        Assert.Equal(2, getListDeclarations.Count);
        Assert.Contains(getListDeclarations, s => s.ContainerKind == "interface" && s.ContainerName == "IFoo" && s.ReturnType == "System.Collections.Generic.List<(int,int)>");
        Assert.Contains(getListDeclarations, s => s.ContainerKind == "class" && s.ContainerName == "Service" && s.ReturnType == "System.Collections.Generic.List<(int,int)>");

        var nestedAsync = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "NestedAsync"));
        Assert.Equal("System.Threading.Tasks.Task<((int A,int B),string Name)>", nestedAsync.ReturnType);
        Assert.Contains("System.Threading.Tasks.Task<((int A, int B), string Name)> NestedAsync()", nestedAsync.Signature);

        var nestedDeclarations = symbols.Where(s => s.Kind == "function" && s.Name == "Nested").ToList();
        Assert.Equal(2, nestedDeclarations.Count);
        Assert.Contains(nestedDeclarations, s => s.ContainerKind == "interface" && s.ContainerName == "IFoo" && s.ReturnType == "System.Threading.Tasks.Task<((int A,int B),string Name)>");
        Assert.Contains(nestedDeclarations, s => s.ContainerKind == "class" && s.ContainerName == "Service" && s.ReturnType == "System.Threading.Tasks.Task<((int A,int B),string Name)>");

        var tooDeepAsync = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "TooDeepAsync"));
        Assert.Equal("System.Threading.Tasks.Task<(((int A,int B),int C),string Name)>", tooDeepAsync.ReturnType);
        Assert.Contains("System.Threading.Tasks.Task<(((int A, int B), int C), string Name)> TooDeepAsync()", tooDeepAsync.Signature);

        var tooDeepDeclarations = symbols.Where(s => s.Kind == "function" && s.Name == "TooDeep").ToList();
        Assert.Equal(2, tooDeepDeclarations.Count);
        Assert.Contains(tooDeepDeclarations, s => s.ContainerKind == "interface" && s.ContainerName == "IFoo" && s.ReturnType == "System.Threading.Tasks.Task<(((int A,int B),int C),string Name)>");
        Assert.Contains(tooDeepDeclarations, s => s.ContainerKind == "class" && s.ContainerName == "Service" && s.ReturnType == "System.Threading.Tasks.Task<(((int A,int B),int C),string Name)>");
    }

    [Fact]
    public void Extract_CSharp_DetectsGenericAttributesOnMethodTypeParameters()
    {
        // Issue #347: method type-parameter lists must survive generic attributes whose bodies
        // contain nested angle brackets; otherwise ordinary methods and explicit-interface
        // implementations disappear from symbols / definition.
        // Issue #347: メソッドの型パラメータ列は、入れ子の angle bracket を含む generic 属性が
        // 付いていても保持されなければならない。そうでないと通常メソッドと explicit-interface
        // 実装が symbols / definition から無言で消える。
        var content = """
            namespace GenericAttr;

            public class GenAttr<T> : System.Attribute { }

            public interface IFoo
            {
                void Run<[GenAttr<System.Collections.Generic.List<int>>] T>(T value);
            }

            public class Tagged : IFoo
            {
                public void M<[GenAttr<int>] U>(U u) { }
                public void N<[GenAttr<int>, GenAttr<string>] U>(U u) { }
                public void P<[GenAttr<(int, int)>] U>(U u) { }
                public void Q<[GenAttr<System.Collections.Generic.List<int>>] U>(U u) { }
                public void R<[GenAttr<System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>>] U>(U u) { }
                void IFoo.Run<[GenAttr<System.Collections.Generic.List<int>>] T>(T value) { }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var methodNames = symbols.Where(s => s.Kind == "function").Select(s => s.Name).ToList();
        Assert.Contains("M", methodNames);
        Assert.Contains("N", methodNames);
        Assert.Contains("P", methodNames);
        Assert.Contains("Q", methodNames);
        Assert.Contains("R", methodNames);

        var m = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "M"));
        Assert.Equal("class", m.ContainerKind);
        Assert.Equal("Tagged", m.ContainerName);
        Assert.Equal("public void M<[GenAttr<int>] U>(U u) { }", m.Signature);

        var n = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "N"));
        Assert.Equal("public void N<[GenAttr<int>, GenAttr<string>] U>(U u) { }", n.Signature);

        var p = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "P"));
        Assert.Equal("public void P<[GenAttr<(int, int)>] U>(U u) { }", p.Signature);

        var q = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Q"));
        Assert.Equal("public void Q<[GenAttr<System.Collections.Generic.List<int>>] U>(U u) { }", q.Signature);

        var r = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "R"));
        Assert.Equal("public void R<[GenAttr<System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>>] U>(U u) { }", r.Signature);

        var runDeclarations = symbols.Where(s => s.Kind == "function" && s.Name == "Run").ToList();
        Assert.Equal(2, runDeclarations.Count);
        Assert.Contains(runDeclarations, s => s.ContainerKind == "interface" && s.ContainerName == "IFoo" && s.Signature == "void Run<[GenAttr<System.Collections.Generic.List<int>>] T>(T value);");
        Assert.Contains(runDeclarations, s => s.ContainerKind == "class" && s.ContainerName == "Tagged" && s.Signature == "void IFoo.Run<[GenAttr<System.Collections.Generic.List<int>>] T>(T value) { }");
    }

    [Fact]
    public void Extract_CSharp_DetectsExplicitInterfacePropertyImpl()
    {
        // Issue #333: explicit-interface property implementations must be indexed just like
        // their method counterparts, in both brace-body and expression-body forms, including
        // generic interface qualifiers and alias-qualified / generic return types.
        // Issue #333: explicit-interface プロパティ実装も、メソッド側と同じく brace body / expression body
        // の両形式、generic interface 修飾子、alias-qualified / generic な戻り値型でインデックスされること。
        var content = """
            using System.Collections.Generic;
            namespace Demo;

            public interface IThing
            {
                int Value { get; set; }
                string Name { get; }
            }

            public interface IBucket<T>
            {
                IReadOnlyList<T> Items { get; }
            }

            public class Svc : IThing, IBucket<int>
            {
                int IThing.Value { get; set; }
                string IThing.Name => "x";
                IReadOnlyList<int> IBucket<int>.Items => new List<int>();
                ref readonly int IThing.Ref => ref _field;
                int IMap<string, int>.PairCount => 2;
                string IFoo<string?>.Nullable => "n";
                string IFoo<int[]>.ArrayArg => "a";
                private int _field;

                public int Ordinary { get; set; }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var svcProps = symbols.Where(s => s.Kind == "property" && s.ContainerName == "Svc").ToList();

        var value = Assert.Single(svcProps, s => s.Name == "Value");
        Assert.Equal("int", value.ReturnType);

        var name = Assert.Single(svcProps, s => s.Name == "Name");
        Assert.Equal("string", name.ReturnType);

        var items = Assert.Single(svcProps, s => s.Name == "Items");
        Assert.Equal("IReadOnlyList<int>", items.ReturnType);

        var refProp = Assert.Single(svcProps, s => s.Name == "Ref");
        Assert.Equal("int", refProp.ReturnType);

        // Multi-argument generic qualifier (`IMap<string, int>.PairCount`) and single-arg
        // generic qualifiers that embed nullable / array types — all three were silently
        // dropped before the qualifier pattern was widened.
        // 複数型引数の generic qualifier (`IMap<string, int>.PairCount`) と、単一型引数でも
        // nullable / array を内包する qualifier は、qualifier パターン拡張前は黙って消えていた。
        var pairCount = Assert.Single(svcProps, s => s.Name == "PairCount");
        Assert.Equal("int", pairCount.ReturnType);

        var nullable = Assert.Single(svcProps, s => s.Name == "Nullable");
        Assert.Equal("string", nullable.ReturnType);

        var arrayArg = Assert.Single(svcProps, s => s.Name == "ArrayArg");
        Assert.Equal("string", arrayArg.ReturnType);

        // Sanity: the ordinary property still lands exactly once, and the interface-side property
        // declarations remain present in the symbol set (two entries each for Value / Items — the
        // interface member and its explicit impl).
        // Sanity: 通常 property も 1 件のまま、interface 側の property 宣言も引き続き抽出される
        // （Value / Items は interface メンバー分と explicit 実装分の 2 件ずつが残る）。
        Assert.Single(svcProps, s => s.Name == "Ordinary");
        Assert.Equal(2, symbols.Count(s => s.Kind == "property" && s.Name == "Value"));
        Assert.Equal(2, symbols.Count(s => s.Kind == "property" && s.Name == "Items"));
    }

    [Fact]
    public void Extract_CSharp_DetectsExplicitInterfaceEventImpl()
    {
        // Issue #351: explicit-interface events must emit the trailing event name (`Evt`)
        // instead of dropping the implementation or inventing the qualifier (`IFoo`) as a
        // phantom event. Cover same-line and next-line accessor blocks plus generic/global
        // qualifiers to keep the dedicated event row aligned with the other explicit-member rows.
        // Issue #351: 明示的インターフェース event は、実装を落としたり qualifier (`IFoo`) を
        // 幻の event 名として emit したりせず、末尾の event 名 (`Evt`) を記録しなければならない。
        // 専用 event 行が他の explicit-member 行と揃うよう、同一行/次行 accessor block と
        // generic/global qualifier をまとめて守る。
        var content = """
            namespace Demo;

            public interface IFoo
            {
                event System.EventHandler Evt;
                event System.EventHandler Evt2;
                event System.EventHandler Evt3;
                event System.EventHandler Evt4;
            }

            public class Svc : IFoo
            {
                event System.EventHandler IFoo.Evt
                {
                    add { }
                    remove { }
                }

                event System.EventHandler IFoo.Evt2 { add { } remove { } }
                event System.EventHandler IMap<string, int>.Evt3 { add { } remove { } }
                event System.EventHandler global::Demo.IFoo.Evt4 { add { } remove { } }
                public event System.EventHandler OnBaseline;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Equal(5, symbols.Count(s => s.Kind == "event" && s.ContainerName == "Svc"));
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "Evt" && s.ContainerName == "Svc");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "Evt2" && s.ContainerName == "Svc");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "Evt3" && s.ContainerName == "Svc");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "Evt4" && s.ContainerName == "Svc");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "OnBaseline" && s.ContainerName == "Svc");
        Assert.DoesNotContain(symbols, s => s.Kind == "event" && s.Name == "IFoo" && s.ContainerName == "Svc");
        Assert.DoesNotContain(symbols, s => s.Kind == "event" && s.Name == "IMap" && s.ContainerName == "Svc");
    }

    [Fact]
    public void Extract_CSharp_DetectsIndexer()
    {
        var content = "public class Collection\n{\n    public string this[int index]\n    {\n        get => _items[index];\n        set => _items[index] = value;\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var indexer = symbols.FirstOrDefault(s => s.Name == "Item");
        Assert.NotNull(indexer);
        Assert.Equal("function", indexer.Kind);
        Assert.Equal("string", indexer.ReturnType);
    }

    [Fact]
    public void Extract_CSharp_DetectsMultiLineIndexer()
    {
        // #293 follow-up: `StripMultiLineCSharpAttributeInterior` must only blank
        // attribute-position `[`. `public int this[\n int i\n] => _items[i];` opens `[`
        // right after the `this` keyword, which is an indexer parameter list, not
        // an attribute. If that `[` were blanked, the indexer would silently drop
        // out of symbol extraction.
        // #293 追加対応: `StripMultiLineCSharpAttributeInterior` は属性位置の `[` だけを
        // 空白化する必要がある。`public int this[\n int i\n] => _items[i];` の `[` は
        // インデクサのパラメータリストであり属性ではない。ここを空白化するとインデクサが
        // シンボル抽出から静かに消える。
        var content =
            "public class Collection\n" +
            "{\n" +
            "    private int[] _items = new int[10];\n" +
            "    public int this[\n" +
            "        int i\n" +
            "    ] => _items[i];\n" +
            "}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var indexer = symbols.FirstOrDefault(s => s.Name == "Item");
        Assert.NotNull(indexer);
        Assert.Equal("function", indexer.Kind);
        Assert.Equal("int", indexer.ReturnType);
    }

    [Fact]
    public void Extract_CSharp_DetectsOperatorOverloads()
    {
        var content = "using System.Collections.Generic;\npublic unsafe struct Money\n{\n    public static (int whole, int cents) operator +(Money a, Money b) => (0, 0);\n    public static Dictionary<string, int> operator -(Money a, Money b) => new();\n    public static bool operator ==(Money a, Money b) => true;\n    public static checked Money operator checked +(Money a, Money b) => new();\n    public static implicit operator decimal(Money m) => 0m;\n    public static explicit operator Money(decimal d) => new();\n    public static explicit operator checked byte(Money m) => 0;\n    public static explicit operator Dictionary<string,int>(Money m) => new();\n    public static explicit operator (int whole,int cents)(Money m) => (0, 0);\n    public static explicit operator (Dictionary<string, int> map, int count)?(Money m) => null;\n    public static explicit operator (int[] items, int count)(Money m) => ([], 0);\n    public static explicit operator ((int a, int b) pair, int count)(Money m) => ((0, 0), 0);\n    public static unsafe explicit operator int*(Money m) => (int*)0;\n    public static unsafe explicit operator delegate* unmanaged[Cdecl]<int, void>(Money m) => (delegate* unmanaged[Cdecl]<int, void>)0;\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Money");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "operator +");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "operator -");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "operator ==");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "operator checked +");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "implicit operator decimal");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "explicit operator Money");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "explicit operator checked byte");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "explicit operator Dictionary<string, int>");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "explicit operator (int whole, int cents)");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "explicit operator (Dictionary<string, int> map, int count)?");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "explicit operator (int[] items, int count)");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "explicit operator ((int a, int b) pair, int count)");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "explicit operator int*");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "explicit operator delegate* unmanaged[Cdecl]<int, void>");
    }

    [Fact]
    public void Extract_CSharp_DetectsCheckedOperators()
    {
        // Issue #238: C# 11 user-defined checked operators (unary, binary, and explicit
        // conversion) must be indexed alongside their unchecked counterparts instead of
        // being silently dropped, and the `checked` keyword must survive into the symbol
        // name so AI clients can disambiguate the two overloads.
        // Issue #238: C# 11 のユーザー定義 `operator checked` (単項 / 二項 / 明示的変換) は
        // unchecked 版と両方インデックスされ、`checked` の有無がシンボル名に残ることで
        // AI クライアントがオーバーロードを区別できるようにする。
        var content = """
            namespace Demo;

            public struct N
            {
                public int V;
                public static N operator +(N a, N b) => new() { V = a.V + b.V };
                public static N operator checked +(N a, N b) => checked(new() { V = a.V + b.V });
                public static N operator -(N a, N b) => new() { V = a.V - b.V };
                public static N operator checked -(N a, N b) => checked(new() { V = a.V - b.V });
                public static N operator -(N a) => new() { V = -a.V };
                public static N operator checked -(N a) => checked(new() { V = -a.V });
                public static explicit operator int(N n) => n.V;
                public static explicit operator checked int(N n) => checked((int)n.V);
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "operator +");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "operator checked +");
        Assert.Equal(2, symbols.Count(s => s.Kind == "function" && s.Name == "operator -"));
        Assert.Equal(2, symbols.Count(s => s.Kind == "function" && s.Name == "operator checked -"));
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "explicit operator int");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "explicit operator checked int");
    }

    [Fact]
    public void Extract_CSharp_DetectsStaticAbstractInterfaceOperators()
    {
        // Issue #244: C# 11 `static abstract` / `abstract static` interface operator members
        // (the foundation of `System.Numerics.INumber<TSelf>` generic math) were silently
        // dropped because the conversion and binary/unary operator regexes only accepted
        // `static|unsafe|extern` in their modifier slot. Both modifier orders and both
        // regular operators and implicit/explicit conversion operators must now be captured
        // the same way the struct-level counterparts are.
        // Issue #244: C# 11 の `static abstract` / `abstract static` interface operator
        // （`System.Numerics.INumber<TSelf>` などの generic math 基盤）は、変換演算子と
        // 二項/単項演算子の正規表現が modifier スロットで `static|unsafe|extern` しか
        // 受け付けていなかったため黙って取りこぼされていた。両方の修飾子順序と、
        // 通常演算子・implicit/explicit 変換演算子の両方を struct 側と同様に捕捉する。
        var content = """
            namespace Demo;

            public interface IMath<T> where T : IMath<T>
            {
                static abstract T operator +(T a, T b);
                abstract static T operator -(T a, T b);
                static abstract T operator *(T a, T b);
                abstract static T Zero { get; }
                static abstract implicit operator T(int x);
                abstract static explicit operator int(T t);
                abstract static int Compare(T a, T b);
                static abstract T operator checked +(T a, T b);
                abstract static explicit operator checked int(T t);
            }

            public struct N
            {
                public static N operator +(N a, N b) => a;
                public static N operator -(N a, N b) => a;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "IMath");
        Assert.Equal(2, symbols.Count(s => s.Kind == "function" && s.Name == "operator +"));
        Assert.Equal(2, symbols.Count(s => s.Kind == "function" && s.Name == "operator -"));
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "operator *");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "implicit operator T");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "explicit operator int");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Zero");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Compare");
        // Widening the modifier slot also incidentally covers C# 11 user-defined checked
        // operator variants on `static abstract` / `abstract static` interface members,
        // because the existing operator-name group already accepts `checked`. Pin both the
        // binary `operator checked +` and the conversion `explicit operator checked int`
        // so a future narrowing of the modifier slot cannot silently drop these shapes.
        // modifier スロットを広げたことで C# 11 の `operator checked` 変換演算子 /
        // 二項演算子の interface 形態も副次的に抽出されるようになる。既存の
        // operator 名キャプチャが `checked` を含む形を受け入れているため、
        // ここでは二項 `operator checked +` と変換 `explicit operator checked int` の
        // 両方を固定し、将来 modifier スロットが狭められても無言で落ちないようにする。
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "operator checked +");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "explicit operator checked int");
    }

    [Fact]
    public void Extract_CSharp_DetectsPointerReturnTypes()
    {
        // Issue #234: methods with pointer / function-pointer return types must still be indexed.
        // Issue #234: ポインタ / 関数ポインタ戻り値型のメソッドも取りこぼさずインデックスする。
        var content = "namespace Demo;\n\npublic unsafe class FP\n{\n    public int* Get(int[] a) { fixed (int* p = a) { return p; } }\n    public void** Double() => null;\n    public byte* Get1() => null;\n    public delegate*<int, int> Transform() => null;\n    public static unsafe int*[] Arr() => null!;\n    public unsafe void Modify(int* p, int v) { *p = v; }\n    public static unsafe int Deref(int* p) => *p;\n    public int* P { get; set; }\n    public byte* Q => null;\n    public int* this[int i] => null;\n}\n\npublic unsafe delegate int* PointerDelegate(int x);\n";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Get" && s.ReturnType == "int*");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Double" && s.ReturnType == "void**");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Get1" && s.ReturnType == "byte*");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Transform" && s.ReturnType == "delegate*<int, int>");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Arr" && s.ReturnType == "int*[]");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Modify");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Deref");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "P" && s.ReturnType == "int*");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Q" && s.ReturnType == "byte*");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Item" && s.ReturnType == "int*");
        Assert.Contains(symbols, s => s.Kind == "delegate" && s.Name == "PointerDelegate" && s.ReturnType == "int*");
    }

    [Fact]
    public void Extract_CSharp_DetectsExplicitInterfacePointerReturnTypes()
    {
        // Issue #234: explicit-interface implementations with pointer / function-pointer
        // return types must still be indexed, including nested generics inside the
        // function-pointer payload and `delegate* unmanaged[Cdecl]<...>` calling conventions.
        // Issue #234: explicit-interface 実装のポインタ / 関数ポインタ戻り値型も取りこぼさず、
        // function-pointer 内部に入れ子の generic がある場合や `delegate* unmanaged[Cdecl]<...>` でも動くこと。
        var content = """
            namespace Demo;

            public unsafe interface IFoo
            {
                int* Get();
                delegate*<int, int> Transform();
                delegate*<System.Collections.Generic.List<int>, int> TransformNested();
                delegate*<delegate*<int, void>, int> TransformFp();
                delegate* unmanaged[Cdecl]<System.Collections.Generic.List<int>, int> TransformUnmanaged();
                byte** Double();
                int*[] Arr();
            }

            public unsafe class Foo : IFoo
            {
                int* IFoo.Get() => null;
                delegate*<int, int> IFoo.Transform() => null;
                delegate*<System.Collections.Generic.List<int>, int> IFoo.TransformNested() => null;
                delegate*<delegate*<int, void>, int> IFoo.TransformFp() => null;
                delegate* unmanaged[Cdecl]<System.Collections.Generic.List<int>, int> IFoo.TransformUnmanaged() => null;
                byte** IFoo.Double() => null;
                int*[] IFoo.Arr() => null!;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Equal(2, symbols.Count(s => s.Kind == "function" && s.Name == "Get"));
        Assert.Equal(2, symbols.Count(s => s.Kind == "function" && s.Name == "Transform"));
        Assert.Equal(2, symbols.Count(s => s.Kind == "function" && s.Name == "TransformNested"));
        Assert.Equal(2, symbols.Count(s => s.Kind == "function" && s.Name == "TransformFp"));
        Assert.Equal(2, symbols.Count(s => s.Kind == "function" && s.Name == "TransformUnmanaged"));
        Assert.Equal(2, symbols.Count(s => s.Kind == "function" && s.Name == "Double"));
        Assert.Equal(2, symbols.Count(s => s.Kind == "function" && s.Name == "Arr"));

        var impls = symbols.Where(s => s.Kind == "function" && s.ContainerName == "Foo").ToList();
        Assert.Equal("int*", impls.Single(s => s.Name == "Get").ReturnType);
        Assert.Equal("delegate*<int, int>", impls.Single(s => s.Name == "Transform").ReturnType);
        Assert.Equal("delegate*<System.Collections.Generic.List<int>, int>", impls.Single(s => s.Name == "TransformNested").ReturnType);
        Assert.Equal("delegate*<delegate*<int, void>, int>", impls.Single(s => s.Name == "TransformFp").ReturnType);
        // Spaces inside `unmanaged[...]<...>` payload are collapsed by CollapseCSharpGenericTypeWhitespace
        // because the outer `<` has `]` as predecessor (a recognized generic-angle start); non-unmanaged
        // `delegate*<...>` keeps payload spaces because the outer `<` has `*` as predecessor.
        // `unmanaged[...]<...>` の payload 内のスペースは CollapseCSharpGenericTypeWhitespace で除去される
        // （outer `<` の直前が `]` で generic angle start と認識されるため）。通常の `delegate*<...>` は
        // outer `<` の直前が `*` で認識されないため payload 内スペースは保持される。
        Assert.Equal("delegate* unmanaged[Cdecl]<System.Collections.Generic.List<int>,int>", impls.Single(s => s.Name == "TransformUnmanaged").ReturnType);
        Assert.Equal("byte**", impls.Single(s => s.Name == "Double").ReturnType);
        Assert.Equal("int*[]", impls.Single(s => s.Name == "Arr").ReturnType);
    }

    [Fact]
    public void Extract_CSharp_DetectsPartialMethods()
    {
        // C# 9 extended partial methods / C# 9 拡張 partial メソッド
        var content = "public partial class App\n{\n    partial void OnInit();\n    public partial string GetName();\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "App");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "OnInit");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetName");
    }

    [Fact]
    public void Extract_CSharp_DetectsGenericMethodOverloads()
    {
        // Issue #41: generic method overloads should both be extracted as definitions
        // Issue #41: ジェネリックメソッドのオーバーロードは両方とも定義として抽出されるべき
        var content = "public class App\n{\n    private static void TryRaise(Action? handler, string context) { }\n    private static void TryRaise<T>(Action<T>? handler, T argument, string context) { }\n    public Task<List<T>> GetItems<T>(int page) { return null; }\n    public void Process<TKey, TValue>(Dictionary<TKey, TValue> map) { }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "TryRaise" && s.Line == 3);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "TryRaise" && s.Line == 4);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetItems");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Process");
    }

    [Fact]
    public void Extract_CSharp_DetectsSpacedAndNestedGenericReturnTypeMethods()
    {
        var content = """
            public class App
            {
                public Task<Result<string, Error>> WithSpace() => null!;
                public Task<Result<string,Error>> NoSpace() => null!;
                public Dictionary<string, List<int>> Map() => new();
                public Tuple<int, int, int, int> Quad() => new(1, 2, 3, 4);
                public Func<int, int, int> Make() => null!;
                public Task<List<Tuple<int, int, int>>> Deep() => null!;
                public (int Left, string Right) Pair() => default;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var withSpace = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "WithSpace"));
        Assert.Equal("Task<Result<string,Error>>", withSpace.ReturnType);

        var noSpace = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "NoSpace"));
        Assert.Equal("Task<Result<string,Error>>", noSpace.ReturnType);

        var map = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Map"));
        Assert.Equal("Dictionary<string,List<int>>", map.ReturnType);

        var quad = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Quad"));
        Assert.Equal("Tuple<int,int,int,int>", quad.ReturnType);

        var make = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Make"));
        Assert.Equal("Func<int,int,int>", make.ReturnType);

        var deep = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Deep"));
        Assert.Equal("Task<List<Tuple<int,int,int>>>", deep.ReturnType);

        var pair = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Pair"));
        Assert.Equal("(int Left, string Right)", pair.ReturnType);
    }

    [Fact]
    public void Extract_CSharp_DetectsSpacedGenericTypeMembersBeyondMethods()
    {
        var content = """
            public delegate Dictionary<string, int> Factory();

            public interface IFoo
            {
                Dictionary<string, int> Get();
            }

            public class Holder : IFoo
            {
                public Dictionary<string, int> Lookup { get; set; } = new();
                public event Action<string, int> OnLog;

                Dictionary<string, int> IFoo.Get() => Lookup;
                public Dictionary<string, int> this[int index] => Lookup;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "delegate" && s.Name == "Factory");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "OnLog" && s.ContainerName == "Holder");

        var lookup = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Lookup"));
        Assert.Equal("Dictionary<string,int>", lookup.ReturnType);
        Assert.Equal("Holder", lookup.ContainerName);

        var indexer = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Item"));
        Assert.Equal("Dictionary<string,int>", indexer.ReturnType);
        Assert.Equal("Holder", indexer.ContainerName);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Get" && s.Line == 5 && s.ReturnType == "Dictionary<string,int>");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Get" && s.ContainerName == "Holder" && s.ReturnType == "Dictionary<string,int>");
    }

    [Fact]
    public void Extract_CSharp_DetectsTupleReturnTypesWithTrailingSuffix()
    {
        // Issue #328: tuple return types with a trailing suffix (`[]`, `?`, `[,]`, `[][]`)
        // must not be silently dropped. The C# returnType alternation's tuple branch
        // needs to carry a trailing `(?:\?|\[[\],\s]*\])*` loop so tuple-array and
        // nullable-tuple members are captured on methods, properties, indexers, and
        // explicit interface implementations.
        // Issue #328: 末尾サフィックス（`[]` / `?` / `[,]` / `[][]`）付きの tuple 戻り値型が
        // サイレントに落ちてはならない。C# の returnType 分岐の tuple 側に
        // `(?:\?|\[[\],\s]*\])*` のループを持たせ、tuple-array / nullable-tuple を
        // メソッド・プロパティ・インデクサ・明示的インターフェース実装で捕捉する。
        var content = """
            namespace Demo;

            public class Svc
            {
                public (int, int)[]        A()  => new (int, int)[0];
                public (int x, int y)[]    B()  => new (int x, int y)[0];
                public (int, int)?         C()  => null;
                public (int x, int y)?     D()  => null;
                public (int, int)[][]      E()  => new (int, int)[0][];
                public (int, int)[,]       F()  => new (int, int)[0, 0];
                public (int, int)?[]       G()  => null!;
                public (int, int)[]? H()         => null;
                public (int, int)[] Ap { get; set; } = System.Array.Empty<(int, int)>();
                public (int, int)? Np { get; set; }
                public (int, int)[] Fp => new (int, int)[0];
                public (int, int)[] this[int index] => Ap;
                (int, int)? ICoord.MaybeFind(string key) => null;
                (int, int)[] ICoord.FindAll(string key) => System.Array.Empty<(int, int)>();
                public (int, int) Plain() => (0, 0);
                public (int, int) PlainProp { get; set; }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var a = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "A"));
        Assert.Equal("(int, int)[]", a.ReturnType);

        var b = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "B"));
        Assert.Equal("(int x, int y)[]", b.ReturnType);

        var c = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "C"));
        Assert.Equal("(int, int)?", c.ReturnType);

        var d = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "D"));
        Assert.Equal("(int x, int y)?", d.ReturnType);

        var e = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "E"));
        Assert.Equal("(int, int)[][]", e.ReturnType);

        var f = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "F"));
        Assert.Equal("(int, int)[,]", f.ReturnType);

        var g = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "G"));
        Assert.Equal("(int, int)?[]", g.ReturnType);

        var h = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "H"));
        Assert.Equal("(int, int)[]?", h.ReturnType);

        var ap = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Ap"));
        Assert.Equal("(int, int)[]", ap.ReturnType);

        var np = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Np"));
        Assert.Equal("(int, int)?", np.ReturnType);

        var fp = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Fp"));
        Assert.Equal("(int, int)[]", fp.ReturnType);

        var indexer = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Item" && s.ContainerName == "Svc"));
        Assert.Equal("(int, int)[]", indexer.ReturnType);

        var maybeFindImpl = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "MaybeFind" && s.ContainerName == "Svc"));
        Assert.Equal("(int, int)?", maybeFindImpl.ReturnType);

        var findAllImpl = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "FindAll" && s.ContainerName == "Svc"));
        Assert.Equal("(int, int)[]", findAllImpl.ReturnType);

        // Regression: plain tuple without a suffix still captured.
        // 回帰: サフィックスなしの素の tuple も引き続き捕捉される。
        var plain = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Plain"));
        Assert.Equal("(int, int)", plain.ReturnType);

        var plainProp = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "PlainProp"));
        Assert.Equal("(int, int)", plainProp.ReturnType);
    }

    [Fact]
    public void Extract_CSharp_DetectsFileScopedType()
    {
        // C# 11 file-scoped type / C# 11 のファイルスコープ型
        var content = "file class InternalHelper\n{\n    file static void DoWork() { }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "InternalHelper");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "DoWork");
    }

    [Fact]
    public void Extract_CSharp_DetectsRefStruct()
    {
        var content = "public readonly ref struct Span2D<T> { }\nref struct StackBuffer { }\npublic readonly struct ImmutablePoint { }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Span2D");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "StackBuffer");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "ImmutablePoint");
    }

    [Fact]
    public void Extract_CSharp_DetectsRefReturnMethodsPropertiesAndIndexers()
    {
        var content = """
            public interface IRefBox
            {
                ref int GetRef();
                ref readonly int GetRefReadonly();
            }

            public class RefBox : IRefBox
            {
                private static int _value;
                private static readonly int[] _items = [1, 2, 3];

                public static ref int RefReturn(ref int[] arr, int i) => ref arr[i];
                public static ref readonly int RefReadonlyReturn(int[] arr, int i) => ref arr[i];
                public ref int PropRef => ref _value;
                public ref readonly int PropRefRo { get => ref _value; }
                public ref readonly int this[int index] => ref _items[index];
                ref int IRefBox.GetRef() => ref _value;
                ref readonly int IRefBox.GetRefReadonly() => ref _value;
            }

            public struct RefReadonlyMembers
            {
                private static int _value;

                public readonly ref readonly int PropReadonly => ref _value;
                public readonly ref readonly int this[int index] => ref _value;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "RefReturn" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "RefReadonlyReturn" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "PropRef" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "PropRefRo" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "PropReadonly" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Item" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Item" && s.Signature != null && s.Signature.Contains("public readonly ref readonly int this[int index]"));

        var explicitGetRef = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "GetRef" && s.ContainerName == "RefBox"));
        Assert.Equal("int", explicitGetRef.ReturnType);
        Assert.Contains("ref int IRefBox.GetRef()", explicitGetRef.Signature);

        var explicitGetRefReadonly = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "GetRefReadonly" && s.ContainerName == "RefBox"));
        Assert.Equal("int", explicitGetRefReadonly.ReturnType);
        Assert.Contains("ref readonly int IRefBox.GetRefReadonly()", explicitGetRefReadonly.Signature);
    }

    [Fact]
    public void Extract_CSharp_DetectsEnumMembers()
    {
        var content = "public enum Color\n{\n    Red,\n    Green = 1,\n    Blue = 2,\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Color");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Red");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Green");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Blue");
    }

    [Fact]
    public void Extract_CSharp_DetectsCompactAndZeroIndentEnumMembers()
    {
        var content = "namespace Demo;\n\npublic enum Compact { A, B = A }\npublic enum Flat\n{\nC,\nD = C\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var compactA = Assert.Single(symbols.Where(s => s.Kind == "enum" && s.Name == "A"));
        var compactB = Assert.Single(symbols.Where(s => s.Kind == "enum" && s.Name == "B"));

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Compact");
        Assert.Equal("enum", compactA.ContainerKind);
        Assert.Equal("Compact", compactA.ContainerName);
        Assert.Equal("Demo.Compact", compactA.ContainerQualifiedName);
        Assert.Equal("enum", compactB.ContainerKind);
        Assert.Equal("Compact", compactB.ContainerName);
        Assert.Equal("Demo.Compact", compactB.ContainerQualifiedName);
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Flat");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "C");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "D");
    }

    [Fact]
    public void Extract_CSharp_DetectsSameLineSiblingEnums()
    {
        var content = "namespace Demo;\n\npublic enum InlineA { A1 } public enum InlineB { B1 }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var a1 = Assert.Single(symbols.Where(s => s.Kind == "enum" && s.Name == "A1"));
        var b1 = Assert.Single(symbols.Where(s => s.Kind == "enum" && s.Name == "B1"));

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "InlineA");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "InlineB");
        Assert.Equal("InlineA", a1.ContainerName);
        Assert.Equal("Demo.InlineA", a1.ContainerQualifiedName);
        Assert.Equal("InlineB", b1.ContainerName);
        Assert.Equal("Demo.InlineB", b1.ContainerQualifiedName);
    }

    [Fact]
    public void Extract_CSharp_DetectsCompactEnumMembersWithAttributesAndCastValues()
    {
        var content = "public enum Mode { [Obsolete] A = (int)B, [EnumMember(Value = \"b\")] B = (MyFlags)(A | C), C = 1 }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Mode");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "A");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "B");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "C");
    }

    [Fact]
    public void Extract_CSharp_DetectsEnumMembersAcrossDirectiveLines()
    {
        var content = "public enum Mode\n{\n#if DEBUG\n    A,\n#endif\n#region values\n    B,\n#endregion\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var memberA = Assert.Single(symbols.Where(s => s.Kind == "enum" && s.Name == "A"));
        var memberB = Assert.Single(symbols.Where(s => s.Kind == "enum" && s.Name == "B"));

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Mode");
        Assert.Equal("enum", memberA.ContainerKind);
        Assert.Equal("Mode", memberA.ContainerName);
        Assert.Equal("enum", memberB.ContainerKind);
        Assert.Equal("Mode", memberB.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_TrimsClosingBraceFromFinalEnumMemberSpan()
    {
        var content = "public enum Status\n{\n    Ready,\n    Busy\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var busy = Assert.Single(symbols.Where(s => s.Kind == "enum" && s.Name == "Busy"));

        Assert.Equal("Busy", busy.Signature);
        Assert.Equal(4, busy.EndLine);
        Assert.DoesNotContain("}", busy.Signature);
    }

    [Fact]
    public void Extract_CSharp_DetectsLowercaseAndUnicodeEnumMembers()
    {
        var content = "public enum Status\n{\n    active,\n    inactive,\n    Δelta = active,\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Status");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "active");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "inactive");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Δelta");
    }

    [Fact]
    public void Extract_CSharp_DetectsEnumMembersWhenAttributeSharesDeclarationLine()
    {
        var content = "[Flags] public enum Mode\n{\n    A,\n    B = A,\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Mode");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "A");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "B");
    }

    [Fact]
    public void Extract_CSharp_DetectsEnumMembersWhenMemberAttributesShareLine()
    {
        var content = "public enum Mode\n{\n    [Obsolete] A,\n    [EnumMember(Value = \"a\")] B = A,\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Mode");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "A");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "B");
    }

    [Fact]
    public void Extract_CSharp_DoesNotTreatMultilineEnumMemberAttributeArgumentsAsMembers()
    {
        var content = "using System.Runtime.Serialization;\n\npublic enum Mode\n{\n    [EnumMember(\n        Value = Alias,\n        Other = 1)]\n    A,\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.DoesNotContain(symbols, s => s.Name == "Value");
        Assert.DoesNotContain(symbols, s => s.Name == "Other");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "A");
    }

    [Fact]
    public void Extract_CSharp_DetectsTabIndentedEnumMembers()
    {
        var content = "public enum Mode\n{\n\tA,\n\tB = A,\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "A");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "B");
    }

    [Fact]
    public void Extract_CSharp_RecoversAfterIncompleteEnumDeclarationAttribute()
    {
        var content = "[Attr(\npublic enum Mode\n{\n    A,\n}\n\npublic class After\n{\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Mode");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "A");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "After");
    }

    [Fact]
    public void Extract_CSharp_RecoversAfterIncompleteEnumMemberAttribute()
    {
        var content = "public enum Mode\n{\n    [Attr()\n    A,\n    B\n}\n\npublic class After\n{\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "A");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "B");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "After");
    }

    [Fact]
    public void Extract_CSharp_RecoversAfterIncompleteEnumMemberAttributeMissingParen()
    {
        var content = "public enum BrokenAttr\n{\n    [Attr(\n    X,\n    Y\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var memberY = Assert.Single(symbols.Where(s => s.Kind == "enum" && s.Name == "Y"));

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "BrokenAttr");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "X");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Y");
        Assert.Equal("Y", memberY.Signature);
        Assert.Equal(5, memberY.EndLine);
        Assert.DoesNotContain("}", memberY.Signature);
    }

    [Fact]
    public void Extract_CSharp_RecoversModifierlessMembersAfterIncompleteAttribute()
    {
        var content = "public class C\n{\n    [Attr(\n    void M() {}\n    string Name { get; }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "M");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Name");
    }

    [Fact]
    public void Extract_CSharp_RecoversBracketTypedMembersAfterIncompleteAttribute()
    {
        var content = "public class C\n{\n    [Attr(\n    int[] Values { get; }\n    int[] Build() => [];\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Values");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Build");
    }

    [Fact]
    public void Extract_CSharp_EnumMembersTrackOwningEnum()
    {
        var content = "namespace Demo;\n\npublic enum First\n{\n    None,\n}\n\npublic enum Second\n{\n    None,\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var noneMembers = symbols
            .Where(s => s.Kind == "enum" && s.Name == "None")
            .OrderBy(s => s.Line)
            .ToList();

        Assert.Equal(2, noneMembers.Count);
        Assert.Equal("enum", noneMembers[0].ContainerKind);
        Assert.Equal("First", noneMembers[0].ContainerName);
        Assert.Equal("Demo.First", noneMembers[0].ContainerQualifiedName);
        Assert.Equal("enum", noneMembers[1].ContainerKind);
        Assert.Equal("Second", noneMembers[1].ContainerName);
        Assert.Equal("Demo.Second", noneMembers[1].ContainerQualifiedName);
    }

    [Fact]
    public void Extract_CSharp_EnumMemberDoesNotMatchObjectInitializer()
    {
        // Object initializer lines should not be extracted as enum members
        // オブジェクト初期化子行は enum メンバーとして抽出されないこと
        var content = "var user = new User\n{\n    Name = \"Alice\",\n    Age = 30,\n    Email = GetEmail(),\n};";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.DoesNotContain(symbols, s => s.Name == "Name");
        Assert.DoesNotContain(symbols, s => s.Name == "Age");
        Assert.DoesNotContain(symbols, s => s.Name == "Email");
    }

    [Fact]
    public void Extract_CSharp_Issue374_ObjectInitializerNumericAssignmentsDoNotCreatePhantomSymbols()
    {
        // Closes #374: the full issue repro uses both multiline and inline object initializers
        // with numeric assignments like `Age = 30,` and `Priority = 2`. Those lines must not
        // reappear as phantom enum-member symbols just because they share the old `Name = 1,`
        // surface shape.
        // Closes #374: issue 本文の再現ケースでは `Age = 30,` や `Priority = 2` のような
        // 数値代入を含む multiline / inline object initializer が混在する。旧来の
        // `Name = 1,` 形に見えても phantom enum-member symbol を再発させてはいけない。
        var content = """
            using System.Collections.Generic;

            namespace CsObjInitPhantom;

            public class Person
            {
                public string Name { get; set; } = "";
                public int Age { get; set; }
                public int Priority { get; set; }
            }

            public class Creator
            {
                public Person CreatePerson() => new Person
                {
                    Name = "Alice",
                    Age = 30,
                    Priority = 1
                };

                public List<Person> CreateMany() => new()
                {
                    new Person { Name = "Bob", Age = 25, Priority = 2 },
                    new Person
                    {
                        Name = "Carol",
                        Age = 45,
                        Priority = 3
                    }
                };
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var ageSymbols = symbols.Where(s => s.Name == "Age").ToList();
        Assert.Single(ageSymbols);
        Assert.Equal("property", ageSymbols[0].Kind);
        Assert.Equal("Person", ageSymbols[0].ContainerName);

        var prioritySymbols = symbols.Where(s => s.Name == "Priority").ToList();
        Assert.Single(prioritySymbols);
        Assert.Equal("property", prioritySymbols[0].Kind);
        Assert.Equal("Person", prioritySymbols[0].ContainerName);

        var nameSymbols = symbols.Where(s => s.Name == "Name").ToList();
        Assert.Single(nameSymbols);
        Assert.Equal("property", nameSymbols[0].Kind);
        Assert.Equal("Person", nameSymbols[0].ContainerName);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "CreatePerson" && s.ContainerName == "Creator");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "CreateMany" && s.ContainerName == "Creator");
    }

    [Fact]
    public void Extract_CSharp_Issue357_EnumMembersWithComplexConstantExpressionsStayIndexed()
    {
        // Current main already captures these enum members, but the open issue fixture was not
        // pinned in tests. Keep the exact value-shape mix here so future regex tightening does
        // not silently re-drop member-access, cast, or parenthesized constant expressions.
        // 現在の main はこれらの enum member を抽出できるが、open issue の fixture 自体は
        // テストで固定されていなかった。将来の regex 調整で member access / cast /
        // parenthesized constant expression が黙って再脱落しないよう、この value-shape
        // の混在をここで固定する。
        var content = """
            namespace CsEnumComplexValue;

            public class K
            {
                public const int Foo = 1;
            }

            public enum E1
            {
                Plain = 0,
                Hex = 0xFF,
                Combined = Plain | 0,
                Shifted = 1 << 3,
                Arith = 1 + 2,
                ConstRef = K.Foo,
                Casted = (int)1.5,
                Paren = (1 + 2),
                CharCast = (int)'A',
                MemberAccess = System.Int32.MaxValue,
            }

            [System.Flags]
            public enum Permissions
            {
                None = 0,
                Read = 1,
                Write = 2,
                All = Read | Write,
                Execute = K.Foo,
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        foreach (var name in new[] { "Plain", "Hex", "Combined", "Shifted", "Arith", "ConstRef", "Casted", "Paren", "CharCast", "MemberAccess" })
        {
            var symbol = Assert.Single(symbols.Where(s => s.Name == name));
            Assert.Equal("enum", symbol.Kind);
            Assert.Equal("E1", symbol.ContainerName);
            Assert.Equal("enum", symbol.ContainerKind);
        }

        foreach (var name in new[] { "None", "Read", "Write", "All", "Execute" })
        {
            var symbol = Assert.Single(symbols.Where(s => s.Name == name));
            Assert.Equal("enum", symbol.Kind);
            Assert.Equal("Permissions", symbol.ContainerName);
            Assert.Equal("enum", symbol.ContainerKind);
        }
    }

    [Fact]
    public void Extract_CSharp_Issue339_SameLineAttributedEnumMembersStayIndexed()
    {
        // Same-line C# attributes on enum members must not hide the member name.
        // C# enum member の同行 attribute は member 名を隠してはならない。
        var content = """
            namespace EnumAttr;

            public enum Status
            {
                [System.Obsolete] Legacy = 0,
                [System.ComponentModel.DefaultValue(1)] B = 1,
                [System.Obsolete][System.ComponentModel.Browsable(false)] D = 3,

                [System.Obsolete]
                E = 4,

                F = 5,
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Status");
        foreach (var name in new[] { "Legacy", "B", "D", "E", "F" })
        {
            var symbol = Assert.Single(symbols.Where(s => s.Name == name));
            Assert.Equal("enum", symbol.Kind);
            Assert.Equal("Status", symbol.ContainerName);
            Assert.Equal("enum", symbol.ContainerKind);
        }
    }

    [Fact]
    public void Extract_CSharp_DetectsRegionDirectives()
    {
        var content = "#region Private Methods\nvoid Helper() { }\n#endregion\n\n#region Properties\npublic int X { get; set; }\n#endregion";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "Private Methods");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "Properties");
    }

    [Fact]
    public void Extract_CSharp_DetectsStaticConstructorAndFinalizer()
    {
        var content = "public class Cache\n{\n    static Cache()\n    {\n        // init\n    }\n\n    ~Cache()\n    {\n        // cleanup\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Cache");
        // Static constructor — extracted by the static ctor pattern
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Cache" && s.Line == 3);
        // Finalizer
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Cache" && s.Line == 8);
    }

    [Fact]
    public void Extract_CSharp_RangeIgnoresLiteralBracesInsideMethodBodies()
    {
        var content = """
            public class BraceExamples
            {
                public void First()
                {
                    var open = "{";
                    var close = '}';
                    var interpolated = $"{{";
                    var verbatim = @"{";
                }

                public void Second()
                {
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var first = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "First"));
        var second = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Second"));
        Assert.Equal(3, first.Line);
        Assert.Equal(9, first.EndLine);
        Assert.Equal(11, second.Line);
        Assert.Equal(13, second.EndLine);
        Assert.Equal(2, symbols.Count(s => s.Kind == "function"));
    }

    [Fact]
    public void Extract_CSharp_DoesNotExtractSymbolsFromRawStringsVerbatimStringsOrComments()
    {
        var content = """"
            public class RealExample
            {
                public const string RawFixture = """
                    class Hidden
                    {
                        public void Fake()
                        {
                        }
                    }
                    """;

                public const string VerbatimFixture = @"class AlsoHidden
            {
                public void FakeVerbatim()
                {
                }
            }";

                /*
                class CommentHidden
                {
                    public void FakeComment()
                    {
                    }
                }
                */

                public void Keep()
                {
                }
            }
            """";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "RealExample");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Keep");
        Assert.DoesNotContain(symbols, s => s.Name == "Hidden");
        Assert.DoesNotContain(symbols, s => s.Name == "Fake");
        Assert.DoesNotContain(symbols, s => s.Name == "AlsoHidden");
        Assert.DoesNotContain(symbols, s => s.Name == "FakeVerbatim");
        Assert.DoesNotContain(symbols, s => s.Name == "CommentHidden");
        Assert.DoesNotContain(symbols, s => s.Name == "FakeComment");
        Assert.Equal(1, symbols.Count(s => s.Kind == "class"));
    }

    [Fact]
    public void Extract_CSharp_DetectsNullableReturnTypeMethods()
    {
        var content = "public static class GitHelper\n{\n    public static string? ResolveGitCommonDir(string projectRoot)\n    {\n        return null;\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var method = Assert.Single(symbols.Where(s => s.Name == "ResolveGitCommonDir"));
        Assert.Equal("function", method.Kind);
        Assert.Equal("string?", method.ReturnType);
    }

    [Fact]
    public void Extract_AssignsContainerAndRanges_ForNestedMembers()
    {
        var content = "public class UserService\n{\n    public async Task<User> GetUser(int id)\n    {\n        return default!;\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var classSymbol = Assert.Single(symbols.Where(s => s.Kind == "class"));
        var method = Assert.Single(symbols.Where(s => s.Kind == "function"));

        Assert.Equal(1, classSymbol.StartLine);
        Assert.Equal(7, classSymbol.EndLine);
        Assert.Equal(2, classSymbol.BodyStartLine);
        Assert.Equal(7, classSymbol.BodyEndLine);
        Assert.Equal("class", method.ContainerKind);
        Assert.Equal("UserService", method.ContainerName);
        Assert.Equal(3, method.StartLine);
        Assert.Equal(6, method.EndLine);
    }

    [Fact]
    public void Extract_Go_DetectsFunctions()
    {
        // Should detect both regular and method functions
        // 通常関数とメソッド関数の両方を検出する
        var content = "func NewHandler() *Handler {\n}\nfunc (h *Handler) ServeHTTP(w http.ResponseWriter, r *http.Request) {\n}";
        var symbols = SymbolExtractor.Extract(1, "go", content);

        Assert.Equal(2, symbols.Count);
        Assert.Contains(symbols, s => s.Name == "NewHandler");
        Assert.Contains(symbols, s => s.Name == "ServeHTTP");
    }

    [Fact]
    public void Extract_Go_DetectsGenericTypeDeclarations()
    {
        var content = """
            type Stack[T any] struct {
                items []T
            }

            type Container[T comparable, U any] interface {
                Get() U
            }

            type Alias[T any] string
            """;
        var symbols = SymbolExtractor.Extract(1, "go", content);

        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Stack");
        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "Container");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Alias");
    }

    [Fact]
    public void Extract_Shell_DetectsFunctions()
    {
        var content = "function setup() {\n  echo 'setup'\n}\n\ncleanup() {\n  echo 'cleanup'\n}";
        var symbols = SymbolExtractor.Extract(1, "shell", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "setup");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "cleanup");
    }

    [Fact]
    public void Extract_SQL_DetectsCreateStatements()
    {
        var content = "CREATE TEMP TABLE users (\n  id INT PRIMARY KEY\n);\n\nCREATE OR REPLACE FUNCTION get_user(id INT) RETURNS void;\n\nCREATE MATERIALIZED VIEW active_users AS SELECT * FROM users;\n\nCREATE TYPE color AS ENUM ('red', 'green');\nCREATE TYPE inventory_item AS (name text);\nCREATE SCHEMA analytics;\nCREATE SCHEMA AUTHORIZATION analytics_owner;\nCREATE SEQUENCE order_seq;\nCREATE EXTENSION IF NOT EXISTS pgcrypto;\nCREATE DOMAIN positive_int AS integer CHECK (VALUE > 0);\nCREATE UNIQUE INDEX users_email_idx ON users (id);\n\nALTER TABLE users ADD COLUMN email TEXT;";
        var symbols = SymbolExtractor.Extract(1, "sql", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "users");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "get_user");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "active_users");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "color");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "inventory_item");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "analytics");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "analytics_owner");
        Assert.DoesNotContain(symbols, s => s.Kind == "namespace" && s.Name == "AUTHORIZATION");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "order_seq");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "pgcrypto");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "positive_int");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "users_email_idx");
    }

    [Fact]
    public void Extract_SQL_DoesNotCaptureOnAsAnonymousIndexName()
    {
        var content = "CREATE INDEX ON users (email);\nCREATE INDEX IF NOT EXISTS users_name_idx ON users (name);";
        var symbols = SymbolExtractor.Extract(1, "sql", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "ON");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "users_name_idx");
    }

    [Fact]
    public void Extract_SQL_DetectsTSqlDdlKinds()
    {
        var content =
            "CREATE SCHEMA sales AUTHORIZATION dbo;\n" +
            "CREATE TYPE sales.Money FROM DECIMAL(18, 4) NOT NULL;\n" +
            "CREATE SEQUENCE sales.OrderSeq START WITH 1 INCREMENT BY 1;\n" +
            "CREATE SYNONYM dbo.Customers FOR external.Customers;\n" +
            "CREATE LOGIN [app_service] WITH PASSWORD = 'x';\n" +
            "CREATE USER app_service FOR LOGIN app_service;\n" +
            "CREATE ROLE sales_writer AUTHORIZATION dbo;\n" +
            "CREATE DATABASE sales_db;\n" +
            "CREATE CERTIFICATE svc_cert WITH SUBJECT = 'svc';\n" +
            "CREATE PARTITION FUNCTION pf_OrdersByYear (datetime2) AS RANGE RIGHT FOR VALUES ('2024-01-01');\n" +
            "CREATE PARTITION SCHEME ps_OrdersByYear AS PARTITION pf_OrdersByYear TO ([primary]);\n" +
            "CREATE FULLTEXT CATALOG ftc_sales WITH ACCENT_SENSITIVITY=OFF;\n" +
            "CREATE PROC dbo.sp_DailyReport @Date DATE AS BEGIN SELECT 1; END\n" +
            "CREATE PROCEDURE [dbo].[sp_GetOrder] @Id INT AS SELECT 1;\n" +
            "CREATE OR ALTER PROCEDURE dbo.sp_UpsertUser AS SELECT 1;\n" +
            "CREATE OR ALTER VIEW dbo.v_ActiveOrders AS SELECT 1;\n" +
            "ALTER PROCEDURE dbo.sp_DailyReport AS SELECT 2;\n" +
            "ALTER FUNCTION dbo.fn_Total() RETURNS INT AS BEGIN RETURN 1 END;\n" +
            "ALTER PARTITION FUNCTION pf_OrdersByYear() SPLIT RANGE ('2025-01-01');\n" +
            "ALTER SCHEMA sales TRANSFER dbo.Customers;\n" +
            "ALTER EXTENSION hstore UPDATE TO '1.5';\n" +
            "ALTER CERTIFICATE svc_cert WITH PRIVATE KEY (DECRYPTION BY PASSWORD = 'x');\n" +
            "ALTER DOMAIN us_postal_code DROP NOT NULL;\n";
        var symbols = SymbolExtractor.Extract(1, "sql", content);

        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "sales");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "sales.Money");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "sales.OrderSeq");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "dbo.Customers");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "[app_service]");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "app_service");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "sales_writer");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "sales_db");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "svc_cert");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "pf_OrdersByYear");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "ps_OrdersByYear");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "ftc_sales");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "dbo.sp_DailyReport");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "[dbo].[sp_GetOrder]");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "dbo.sp_UpsertUser");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "dbo.v_ActiveOrders");
        // ALTER on an object kind other than TABLE must now be captured with the same kind
        // contract as the matching CREATE row (function for procedure-like, namespace for schema,
        // import for extension, class for everything else).
        // TABLE 以外のオブジェクトに対する ALTER も、対応する CREATE 行と同じ kind 契約で
        // 捕捉されること（プロシージャ類は function、SCHEMA は namespace、EXTENSION は import、
        // その他は class）。
        Assert.Equal(2, symbols.Count(s => s.Name == "dbo.sp_DailyReport"));
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "dbo.sp_DailyReport" && s.Signature != null && s.Signature.StartsWith("ALTER PROCEDURE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "dbo.fn_Total");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "pf_OrdersByYear" && s.Signature != null && s.Signature.StartsWith("ALTER PARTITION FUNCTION", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "sales" && s.Signature != null && s.Signature.StartsWith("ALTER SCHEMA", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "hstore");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "svc_cert" && s.Signature != null && s.Signature.StartsWith("ALTER CERTIFICATE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "us_postal_code");

        // SQL `CREATE SCHEMA sales;` and `ALTER SCHEMA sales TRANSFER ...;` are body-less
        // namespace-kind symbols. They must NOT be treated as C# file-scoped namespaces and
        // must not wrap every subsequent top-level SQL symbol as their container — SQL
        // schema is expressed through qualified names (`sales.Money`) rather than containment.
        // SQL の `CREATE SCHEMA sales;` と `ALTER SCHEMA sales TRANSFER ...;` は body 無しの
        // namespace kind だが、C# の file-scoped namespace 扱いにしてはならない。SQL の schema
        // は包含ではなく `sales.Money` のような修飾名で表すので、後続の top-level シンボルを
        // schema 配下に吸い込んではいけない。
        var containerPollutionVictims = new[]
        {
            "dbo.fn_Total",
            "pf_OrdersByYear",
            "hstore",
            "us_postal_code",
            "[app_service]",
            "app_service",
            "sales_writer",
            "sales_db",
            "svc_cert",
            "ps_OrdersByYear",
            "ftc_sales",
            "dbo.sp_DailyReport",
            "[dbo].[sp_GetOrder]",
            "dbo.sp_UpsertUser",
            "dbo.v_ActiveOrders",
        };
        foreach (var name in containerPollutionVictims)
        {
            Assert.All(
                symbols.Where(s => s.Name == name),
                s => Assert.True(
                    s.ContainerKind != "namespace" || s.ContainerName != "sales",
                    $"{s.Kind} {s.Name} was wrapped under namespace=sales — ALTER/CREATE SCHEMA must not act as a C# file-scoped namespace container."));
        }
    }

    [Fact]
    public void Extract_SQL_QualifiedNamesAllowWhitespaceAroundDots()
    {
        var content =
            "CREATE PROCEDURE [sales] . [sp_Report] AS SELECT 1;\n" +
            "CREATE VIEW dbo . v_Orders AS SELECT 1;\n" +
            "CREATE TYPE sales . Money AS ENUM ('usd');\n";

        var symbols = SymbolExtractor.Extract(1, "sql", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name.Contains("sp_Report", StringComparison.Ordinal));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name.Contains("v_Orders", StringComparison.Ordinal));
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name.Contains("Money", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_SQL_ProcedureBodyRange_TSqlBeginEnd()
    {
        // Multi-line T-SQL CREATE PROCEDURE with explicit BEGIN/END body terminated by GO.
        // ReferenceExtractor.ResolveContainerForCall depends on BodyStartLine/BodyEndLine
        // covering the lines that hold EXEC / CALL calls inside the procedure (issue #429).
        // GO で終わる複数行 T-SQL CREATE PROCEDURE（BEGIN/END 本体）。
        // ReferenceExtractor.ResolveContainerForCall は本体内の EXEC / CALL を含む行を
        // カバーする BodyStartLine / BodyEndLine に依存する（issue #429）。
        var content =
            "CREATE PROCEDURE dbo.sp_Outer\n" +  // line 1
            "AS\n" +                              // line 2
            "BEGIN\n" +                           // line 3
            "  EXEC dbo.sp_Inner;\n" +            // line 4
            "  SELECT 1;\n" +                     // line 5
            "END\n" +                             // line 6
            "GO\n" +                              // line 7
            "CREATE PROCEDURE dbo.sp_Inner\n" +   // line 8
            "AS\n" +                              // line 9
            "BEGIN\n" +                           // line 10
            "  SELECT 2;\n" +                     // line 11
            "END\n" +                             // line 12
            "GO\n";                               // line 13

        var symbols = SymbolExtractor.Extract(1, "sql", content);

        var outer = Assert.Single(symbols, s => s.Kind == "function" && s.Name == "dbo.sp_Outer");
        Assert.Equal(1, outer.StartLine);
        Assert.NotNull(outer.BodyStartLine);
        Assert.NotNull(outer.BodyEndLine);
        // Body must cover the EXEC call on line 4 so callers/impact can attribute it.
        // EXEC 行（4 行目）を本体が覆う必要がある（callers / impact が帰属させられるように）。
        Assert.True(outer.BodyStartLine!.Value <= 4, $"BodyStartLine={outer.BodyStartLine} must be <= 4");
        Assert.True(outer.BodyEndLine!.Value >= 6, $"BodyEndLine={outer.BodyEndLine} must be >= 6 (body END)");
        // Body must not leak into the next procedure on line 8.
        // 8 行目の次のプロシージャまで本体が伸びてはいけない。
        Assert.True(outer.BodyEndLine!.Value < 8, $"BodyEndLine={outer.BodyEndLine} must not leak into the next CREATE at line 8");

        var inner = Assert.Single(symbols, s => s.Kind == "function" && s.Name == "dbo.sp_Inner");
        Assert.Equal(8, inner.StartLine);
        Assert.NotNull(inner.BodyStartLine);
        Assert.NotNull(inner.BodyEndLine);
        Assert.True(inner.BodyStartLine!.Value <= 11, $"BodyStartLine={inner.BodyStartLine} must cover SELECT on line 11");
        Assert.True(inner.BodyEndLine!.Value >= 12, $"BodyEndLine={inner.BodyEndLine} must cover END on line 12");
    }

    [Fact]
    public void Extract_SQL_ProcedureBodyRange_PostgresDollarQuoted()
    {
        // PostgreSQL CREATE FUNCTION ... AS $$ ... $$ must resolve BodyEndLine to the line
        // containing the closing `$$`, regardless of BEGIN / END / GO / ; inside the body.
        // PostgreSQL の `CREATE FUNCTION ... AS $$ ... $$` は、本体内の BEGIN / END / GO / ;
        // に関係なく、閉じ `$$` の行で BodyEndLine を解決できる必要がある。
        var content =
            "CREATE OR REPLACE FUNCTION public.notify_user(uid INT) RETURNS void AS $$\n" +  // line 1
            "DECLARE msg TEXT;\n" +                                                            // line 2
            "BEGIN\n" +                                                                        // line 3
            "  msg := 'hi; GO -- fake terminator';\n" +                                        // line 4
            "  PERFORM public.enqueue(uid, msg);\n" +                                          // line 5
            "END;\n" +                                                                         // line 6
            "$$ LANGUAGE plpgsql;\n" +                                                         // line 7
            "\n" +                                                                             // line 8
            "CREATE FUNCTION public.enqueue(uid INT, msg TEXT) RETURNS void AS $$\n" +         // line 9
            "BEGIN\n" +                                                                        // line 10
            "  INSERT INTO outbox VALUES (uid, msg);\n" +                                      // line 11
            "END;\n" +                                                                         // line 12
            "$$ LANGUAGE plpgsql;\n";                                                          // line 13

        var symbols = SymbolExtractor.Extract(1, "sql", content);

        var notify = Assert.Single(symbols, s => s.Kind == "function" && s.Name == "public.notify_user");
        Assert.Equal(1, notify.StartLine);
        Assert.NotNull(notify.BodyStartLine);
        Assert.NotNull(notify.BodyEndLine);
        Assert.True(notify.BodyStartLine!.Value <= 5, $"BodyStartLine={notify.BodyStartLine} must cover PERFORM on line 5");
        Assert.Equal(7, notify.BodyEndLine);

        var enqueue = Assert.Single(symbols, s => s.Kind == "function" && s.Name == "public.enqueue");
        Assert.Equal(9, enqueue.StartLine);
        Assert.NotNull(enqueue.BodyStartLine);
        Assert.NotNull(enqueue.BodyEndLine);
        Assert.True(enqueue.BodyStartLine!.Value <= 11, $"BodyStartLine={enqueue.BodyStartLine} must cover INSERT on line 11");
        Assert.Equal(13, enqueue.BodyEndLine);
    }

    [Fact]
    public void Extract_SQL_ProcedureBodyRange_ClosesAtNextCreateWithoutGo()
    {
        // No `GO` between two procedures — the new-DDL-start guard must still close the
        // previous body so the second CREATE's header is not swallowed into sp_First's body.
        // プロシージャ間に `GO` が無い場合でも、次の DDL 行で前のボディを閉じる必要がある
        // （そうしないと次の CREATE 行が sp_First のボディに吸い込まれる）。
        var content =
            "CREATE PROCEDURE dbo.sp_First AS\n" +  // line 1
            "BEGIN\n" +                              // line 2
            "  EXEC dbo.sp_Helper;\n" +              // line 3
            "END\n" +                                // line 4
            "CREATE PROCEDURE dbo.sp_Second AS\n" +  // line 5
            "BEGIN\n" +                              // line 6
            "  SELECT 2;\n" +                        // line 7
            "END\n";                                 // line 8

        var symbols = SymbolExtractor.Extract(1, "sql", content);

        var first = Assert.Single(symbols, s => s.Kind == "function" && s.Name == "dbo.sp_First");
        Assert.NotNull(first.BodyEndLine);
        Assert.True(first.BodyEndLine!.Value <= 4, $"BodyEndLine={first.BodyEndLine} must close before the next CREATE on line 5");

        var second = Assert.Single(symbols, s => s.Kind == "function" && s.Name == "dbo.sp_Second");
        Assert.Equal(5, second.StartLine);
    }

    [Fact]
    public void Extract_SQL_ProcedureBodyRange_SingleLineAsBeginEnd()
    {
        // Single-line `CREATE PROC ... AS BEGIN ... END` must still expose a body range so
        // that any call on that same line (e.g. an EXEC inside a single-line body) can be
        // attributed back to the procedure.
        // 1 行で書かれた `CREATE PROC ... AS BEGIN ... END` でも、同一行の呼び出しを
        // プロシージャに帰属させるため、必ず body range を返す必要がある。
        var content =
            "CREATE PROC dbo.sp_A AS BEGIN EXEC dbo.sp_B; END\n" +
            "CREATE PROC dbo.sp_B AS BEGIN SELECT 1; END\n";

        var symbols = SymbolExtractor.Extract(1, "sql", content);

        var a = Assert.Single(symbols, s => s.Kind == "function" && s.Name == "dbo.sp_A");
        Assert.NotNull(a.BodyStartLine);
        Assert.NotNull(a.BodyEndLine);
        Assert.True(a.BodyStartLine!.Value <= 1 && a.BodyEndLine!.Value >= 1,
            $"single-line sp_A body range must cover line 1 (got [{a.BodyStartLine}, {a.BodyEndLine}])");
        Assert.True(a.BodyEndLine!.Value < 2, $"sp_A body must not leak into sp_B on line 2 (got {a.BodyEndLine})");
    }

    [Fact]
    public void Extract_SQL_ProcedureBodyRange_IgnoresTerminatorsInStringsAndComments()
    {
        // A `GO` inside a string literal or a block comment must not close the body
        // prematurely — MaskSqlLineForBodyScan strips strings/comments before the scan.
        // 文字列や `/* ... */` 内の `GO` で本体を閉じないこと
        // （MaskSqlLineForBodyScan が文字列・コメントを除去するため）。
        var content =
            "CREATE PROCEDURE dbo.sp_NoisyBody AS\n" +       // line 1
            "BEGIN\n" +                                       // line 2
            "  DECLARE @msg NVARCHAR(100) = 'GO ahead';\n" +  // line 3 — 'GO' in string
            "  /* GO */\n" +                                  // line 4 — GO in block comment
            "  -- GO line-comment\n" +                        // line 5 — GO in line comment
            "  EXEC dbo.sp_Target;\n" +                       // line 6
            "END\n" +                                         // line 7
            "GO\n" +                                          // line 8 — real terminator
            "CREATE PROCEDURE dbo.sp_Target AS\n" +           // line 9
            "BEGIN SELECT 1; END\n" +                         // line 10
            "GO\n";                                           // line 11

        var symbols = SymbolExtractor.Extract(1, "sql", content);

        var noisy = Assert.Single(symbols, s => s.Kind == "function" && s.Name == "dbo.sp_NoisyBody");
        Assert.NotNull(noisy.BodyStartLine);
        Assert.NotNull(noisy.BodyEndLine);
        // Body must cover the real EXEC on line 6 and must not stop at the fake GO on line 3/4/5.
        // 本体は line 6 の本物の EXEC を覆い、line 3/4/5 の偽 GO で止まってはいけない。
        Assert.True(noisy.BodyEndLine!.Value >= 6, $"BodyEndLine={noisy.BodyEndLine} must cover the real EXEC on line 6");
        Assert.True(noisy.BodyEndLine!.Value < 9, $"BodyEndLine={noisy.BodyEndLine} must not leak into sp_Target on line 9");

        var target = Assert.Single(symbols, s => s.Kind == "function" && s.Name == "dbo.sp_Target");
        Assert.Equal(9, target.StartLine);
    }

    [Fact]
    public void Extract_SQL_ProcedureBodyRange_AlterProcedureHasBody()
    {
        // ALTER PROCEDURE / ALTER FUNCTION / ALTER TRIGGER share the body shape with CREATE
        // and must get a body range so replacement implementations' inner calls resolve too.
        // ALTER PROCEDURE / ALTER FUNCTION / ALTER TRIGGER は CREATE と本体形状を共有するので、
        // 置換実装内の呼び出しも解決できるよう body range を持つ必要がある。
        var content =
            "ALTER PROCEDURE dbo.sp_Reset AS\n" +   // line 1
            "BEGIN\n" +                              // line 2
            "  EXEC dbo.sp_Clear;\n" +               // line 3
            "END\n" +                                // line 4
            "GO\n";                                  // line 5

        var symbols = SymbolExtractor.Extract(1, "sql", content);

        var reset = Assert.Single(symbols, s => s.Kind == "function" && s.Name == "dbo.sp_Reset");
        Assert.NotNull(reset.BodyStartLine);
        Assert.NotNull(reset.BodyEndLine);
        Assert.True(reset.BodyEndLine!.Value >= 3, $"BodyEndLine={reset.BodyEndLine} must cover the EXEC on line 3");
        Assert.True(reset.BodyEndLine!.Value < 5, $"BodyEndLine={reset.BodyEndLine} must not include the GO batch terminator on line 5");
    }

    [Fact]
    public void Extract_SQL_AlterPartitionFunctionHasNoBody()
    {
        // ALTER PARTITION FUNCTION only changes partition boundaries (no code body), so it
        // must keep BodyStartLine / BodyEndLine unset even though ALTER PROCEDURE / FUNCTION
        // / TRIGGER now resolve a body via SqlProcBody.
        // ALTER PARTITION FUNCTION は境界変更のみ（コード本体なし）のため、
        // ALTER PROCEDURE / FUNCTION / TRIGGER が SqlProcBody で本体を取るようになっても、
        // BodyStartLine / BodyEndLine は null のまま維持する必要がある。
        var content =
            "ALTER PARTITION FUNCTION pf_OrdersByYear() SPLIT RANGE ('2025-01-01');\n" +
            "CREATE PROCEDURE dbo.sp_After AS BEGIN SELECT 1; END\n";

        var symbols = SymbolExtractor.Extract(1, "sql", content);

        var partition = Assert.Single(symbols, s => s.Kind == "function" && s.Name == "pf_OrdersByYear");
        Assert.Null(partition.BodyStartLine);
        Assert.Null(partition.BodyEndLine);
    }

    [Fact]
    public void Extract_SQL_ProcedureBodyRange_DoesNotPolluteContainer()
    {
        // Regression guard for the schema-pollution invariant (existing
        // Extract_SQL_DetectsTSqlDdlKinds) extended to proc-body ranges: a CREATE PROCEDURE
        // with a real body must not wrap the *next* proc as its container.
        // スキーマ汚染防止の不変量（既存の Extract_SQL_DetectsTSqlDdlKinds）を、今回追加した
        // プロシージャ本体にも拡張する。本体を持つ CREATE PROCEDURE が「次の」プロシージャを
        // コンテナとして囲ってはならない。
        var content =
            "CREATE PROCEDURE dbo.sp_First AS\n" +
            "BEGIN\n" +
            "  SELECT 1;\n" +
            "END\n" +
            "GO\n" +
            "CREATE PROCEDURE dbo.sp_Second AS\n" +
            "BEGIN\n" +
            "  SELECT 2;\n" +
            "END\n" +
            "GO\n";

        var symbols = SymbolExtractor.Extract(1, "sql", content);

        var second = Assert.Single(symbols, s => s.Kind == "function" && s.Name == "dbo.sp_Second");
        Assert.True(
            second.ContainerKind != "function" || second.ContainerName != "dbo.sp_First",
            $"dbo.sp_Second was wrapped under container=dbo.sp_First — CREATE PROCEDURE body must not wrap sibling procedures.");
    }

    [Fact]
    public void Extract_SQL_ProcedureBodyRange_BodyInternalCreateTableDoesNotCloseBody()
    {
        // Regression for codex review iteration 1 finding #1: the body terminator heuristic must
        // only react to *another* proc-like DDL header (`CREATE|ALTER|DROP PROCEDURE|PROC|FUNCTION|
        // TRIGGER`), not to ordinary body-internal DDL like `CREATE TABLE #tmp` / `ALTER TABLE`.
        // Otherwise issue #429 re-appears whenever a T-SQL procedure stages a temp table before its
        // real work.
        // codex レビュー iteration 1 指摘 #1 の回帰テスト: 本体終端の判定は別の proc 系ヘッダ
        // （`CREATE|ALTER|DROP PROCEDURE|PROC|FUNCTION|TRIGGER`）のときだけ発火し、本体内の普通の
        // DDL（`CREATE TABLE #tmp` / `ALTER TABLE` など）では閉じてはならない。そうしないと、T-SQL
        // プロシージャが一時テーブルを用意してから実処理する典型パターンで issue #429 が再発する。
        var content =
            "CREATE PROCEDURE dbo.sp_Stage AS\n" +  // line 1
            "BEGIN\n" +                              // line 2
            "  CREATE TABLE #tmp(id INT);\n" +      // line 3 — body-internal DDL, must NOT close
            "  ALTER TABLE #tmp ADD name NVARCHAR(100);\n" + // line 4 — same
            "  EXEC dbo.sp_Inner;\n" +               // line 5 — real EXEC must be inside body
            "END\n" +                                // line 6
            "GO\n" +                                 // line 7
            "CREATE PROCEDURE dbo.sp_Inner AS BEGIN SELECT 1; END\n" + // line 8
            "GO\n";                                  // line 9

        var symbols = SymbolExtractor.Extract(1, "sql", content);

        var stage = Assert.Single(symbols, s => s.Kind == "function" && s.Name == "dbo.sp_Stage");
        Assert.NotNull(stage.BodyStartLine);
        Assert.NotNull(stage.BodyEndLine);
        Assert.True(stage.BodyEndLine!.Value >= 5,
            $"BodyEndLine={stage.BodyEndLine} must cover the EXEC on line 5; body-internal CREATE TABLE / ALTER TABLE must not close the body.");
        Assert.True(stage.BodyEndLine!.Value < 8,
            $"BodyEndLine={stage.BodyEndLine} must not leak into sp_Inner starting on line 8.");
    }

    [Fact]
    public void Extract_SQL_ProcedureBodyRange_MultiLineBlockCommentDoesNotCloseBody()
    {
        // Regression for codex review iteration 1 finding #2: MaskSqlLineForBodyScan threads an
        // `inBlockComment` state across lines, so a bare `GO` or `CREATE` appearing inside a
        // multi-line `/* ... */` block must not close the enclosing procedure body.
        // codex レビュー iteration 1 指摘 #2 の回帰テスト: MaskSqlLineForBodyScan は `inBlockComment`
        // を行間に持ち越し、複数行の `/* ... */` ブロック内に現れる単独 `GO` / `CREATE` では
        // 外側プロシージャ本体を閉じてはならない。
        var content =
            "CREATE PROCEDURE dbo.sp_CommentHeavy AS\n" + // line 1
            "BEGIN\n" +                                     // line 2
            "  /*\n" +                                      // line 3 — block comment opens
            "   GO\n" +                                     // line 4 — bare GO inside comment
            "   CREATE PROCEDURE dbo.sp_FakeInner AS SELECT 0;\n" + // line 5 — fake header inside comment
            "  */\n" +                                      // line 6 — block comment closes
            "  EXEC dbo.sp_Real;\n" +                       // line 7 — real EXEC must be inside body
            "END\n" +                                       // line 8
            "GO\n" +                                        // line 9 — real terminator
            "CREATE PROCEDURE dbo.sp_Real AS BEGIN SELECT 1; END\n" + // line 10
            "GO\n";                                         // line 11

        var symbols = SymbolExtractor.Extract(1, "sql", content);

        var heavy = Assert.Single(symbols, s => s.Kind == "function" && s.Name == "dbo.sp_CommentHeavy");
        Assert.NotNull(heavy.BodyStartLine);
        Assert.NotNull(heavy.BodyEndLine);
        Assert.True(heavy.BodyEndLine!.Value >= 7,
            $"BodyEndLine={heavy.BodyEndLine} must cover the real EXEC on line 7; multi-line block comment must not close the body.");
        Assert.True(heavy.BodyEndLine!.Value < 10,
            $"BodyEndLine={heavy.BodyEndLine} must not leak into sp_Real starting on line 10.");
    }

    [Fact]
    public void Extract_SQL_ProcedureBodyRange_CreateOrAlterClosesPriorBody()
    {
        // Regression for codex review iteration 2 finding #1: SqlTopLevelDdlStartRegex must accept
        // both PostgreSQL `CREATE OR REPLACE PROCEDURE` and T-SQL `CREATE OR ALTER PROCEDURE`
        // (SQL Server 2016+) so a sibling `CREATE OR ALTER PROCEDURE` declaration without an
        // intervening `GO` actually terminates the previous procedure's body range.
        // codex レビュー iteration 2 指摘 #1 の回帰テスト: SqlTopLevelDdlStartRegex は PostgreSQL の
        // `CREATE OR REPLACE PROCEDURE` と T-SQL（SQL Server 2016+）の `CREATE OR ALTER PROCEDURE`
        // の両方を受理し、`GO` 区切りなしの隣接 `CREATE OR ALTER PROCEDURE` 宣言で前 proc の
        // body 範囲を確実に閉じなければならない。
        var content =
            "CREATE OR ALTER PROCEDURE dbo.sp_First AS\n" + // line 1
            "BEGIN\n" +                                     // line 2
            "  EXEC dbo.sp_Inner;\n" +                      // line 3 — must be inside sp_First body
            "END\n" +                                       // line 4
            "CREATE OR ALTER PROCEDURE dbo.sp_Second AS\n" +// line 5 — sibling must close sp_First
            "BEGIN\n" +                                     // line 6
            "  EXEC dbo.sp_Other;\n" +                      // line 7 — must be inside sp_Second body
            "END\n";                                        // line 8

        var symbols = SymbolExtractor.Extract(1, "sql", content);

        var first = Assert.Single(symbols, s => s.Kind == "function" && s.Name == "dbo.sp_First");
        Assert.NotNull(first.BodyStartLine);
        Assert.NotNull(first.BodyEndLine);
        Assert.True(first.BodyEndLine!.Value >= 3,
            $"BodyEndLine={first.BodyEndLine} must cover sp_First's EXEC on line 3.");
        Assert.True(first.BodyEndLine!.Value < 5,
            $"BodyEndLine={first.BodyEndLine} must close before sp_Second begins on line 5; CREATE OR ALTER PROCEDURE sibling must terminate the prior body.");

        var second = Assert.Single(symbols, s => s.Kind == "function" && s.Name == "dbo.sp_Second");
        Assert.NotNull(second.BodyStartLine);
        Assert.NotNull(second.BodyEndLine);
        Assert.True(second.BodyStartLine!.Value >= 5,
            $"BodyStartLine={second.BodyStartLine} for sp_Second must start at or after line 5.");
        Assert.True(second.BodyEndLine!.Value >= 7,
            $"BodyEndLine={second.BodyEndLine} must cover sp_Second's EXEC on line 7.");
    }

    [Fact]
    public void Extract_SQL_ProcedureBodyRange_NestedBlockCommentDoesNotCloseBody()
    {
        // Regression for codex review iteration 2 finding #2: MaskSqlLineForBodyScan must track
        // block-comment depth instead of a plain bool so PostgreSQL-style nested
        // `/* /* ... */ ... */` block comments do not exit on the inner `*/` and re-expose comment-
        // interior `GO` / `CREATE PROCEDURE` tokens that would prematurely close the body.
        // codex レビュー iteration 2 指摘 #2 の回帰テスト: MaskSqlLineForBodyScan は plain bool ではなく
        // ブロックコメント depth を追う必要があり、PostgreSQL 風のネスト `/* /* ... */ ... */` を
        // 内側の `*/` で誤って抜けてはならない。抜けるとコメント内部の `GO` / `CREATE PROCEDURE` が
        // 露出し、本体を早期に閉じてしまう。
        var content =
            "CREATE PROCEDURE dbo.sp_NestedComment AS\n" +    // line 1
            "BEGIN\n" +                                         // line 2
            "  /*\n" +                                          // line 3 — outer block opens
            "   /*\n" +                                         // line 4 — inner block opens
            "     GO\n" +                                       // line 5 — bare GO in nested comment
            "     CREATE PROCEDURE dbo.sp_FakeNested AS SELECT 0;\n" + // line 6 — fake header
            "   */\n" +                                         // line 7 — inner closes; outer still open
            "   GO\n" +                                         // line 8 — still inside outer comment
            "   CREATE PROCEDURE dbo.sp_FakeOuter AS SELECT 0;\n" + // line 9 — still inside outer
            "  */\n" +                                          // line 10 — outer closes
            "  EXEC dbo.sp_RealCall;\n" +                       // line 11 — real EXEC must be inside body
            "END\n" +                                           // line 12
            "GO\n" +                                            // line 13 — real terminator
            "CREATE PROCEDURE dbo.sp_AfterNested AS BEGIN SELECT 1; END\n" + // line 14
            "GO\n";                                             // line 15

        var symbols = SymbolExtractor.Extract(1, "sql", content);

        var nested = Assert.Single(symbols, s => s.Kind == "function" && s.Name == "dbo.sp_NestedComment");
        Assert.NotNull(nested.BodyStartLine);
        Assert.NotNull(nested.BodyEndLine);
        Assert.True(nested.BodyEndLine!.Value >= 11,
            $"BodyEndLine={nested.BodyEndLine} must cover the real EXEC on line 11; nested block comment must not close the body via the inner `*/`.");
        Assert.True(nested.BodyEndLine!.Value < 14,
            $"BodyEndLine={nested.BodyEndLine} must not leak into sp_AfterNested starting on line 14.");
    }

    [Fact]
    public void Extract_SQL_DetectsOraclePlSqlDdlKinds()
    {
        // Oracle PL/SQL — PACKAGE / PACKAGE BODY / TYPE / TYPE BODY / DATABASE LINK / DIRECTORY /
        // CONTEXT / PROFILE must all be captured, and object names may contain `$` / `#`.
        // Oracle PL/SQL — PACKAGE / PACKAGE BODY / TYPE / TYPE BODY / DATABASE LINK / DIRECTORY /
        // CONTEXT / PROFILE を全て捕捉し、オブジェクト名に `$` / `#` を含められる。
        var content =
            "CREATE OR REPLACE PACKAGE orders_pkg IS\n" +
            "  PROCEDURE insert_order(p_id IN NUMBER);\n" +
            "END orders_pkg;\n" +
            "/\n" +
            "CREATE OR REPLACE PACKAGE BODY orders_pkg IS\n" +
            "  PROCEDURE insert_order(p_id IN NUMBER) IS BEGIN NULL; END;\n" +
            "END orders_pkg;\n" +
            "/\n" +
            "CREATE OR REPLACE TYPE address_t AS OBJECT (street VARCHAR2(100));\n" +
            "/\n" +
            "CREATE OR REPLACE TYPE BODY address_t AS\n" +
            "END;\n" +
            "/\n" +
            "CREATE SEQUENCE hr.order_seq START WITH 1 INCREMENT BY 1;\n" +
            "CREATE PUBLIC SYNONYM customer_v FOR schema1.customers;\n" +
            "CREATE PUBLIC DATABASE LINK remote_db CONNECT TO app IDENTIFIED BY \"x\" USING 'REMOTE';\n" +
            "CREATE SHARED PUBLIC DATABASE LINK remote_shared_pub_db CONNECT TO app IDENTIFIED BY \"x\" USING 'REMOTE';\n" +
            "CREATE SHARED DATABASE LINK remote_shared_db CONNECT TO app IDENTIFIED BY \"x\" USING 'REMOTE';\n" +
            "CREATE DIRECTORY data_dir AS '/var/oracle/data';\n" +
            "CREATE CONTEXT app_ctx USING app_pkg;\n" +
            "CREATE PROFILE app_profile LIMIT SESSIONS_PER_USER 5;\n" +
            "CREATE TABLE SYS$ITEMS#1 (id NUMBER);\n" +
            "ALTER PACKAGE orders_pkg COMPILE;\n" +
            "ALTER PACKAGE orders_pkg COMPILE BODY;\n" +
            "ALTER TYPE address_t COMPILE BODY;\n" +
            "ALTER DATABASE LINK remote_db;\n" +
            "ALTER DIRECTORY data_dir AS '/var/oracle/data2';\n" +
            "ALTER PROFILE app_profile LIMIT SESSIONS_PER_USER 10;\n";
        var symbols = SymbolExtractor.Extract(1, "sql", content);

        // PACKAGE spec/body both captured (BODY is not absorbed as the package name)
        // PACKAGE spec / body の両方が取れ、`BODY` が package name に吸い込まれない
        Assert.Equal(2, symbols.Count(s => s.Kind == "class" && s.Name == "orders_pkg" && s.Signature != null && s.Signature.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "orders_pkg" && s.Signature != null && s.Signature.Contains("PACKAGE BODY", StringComparison.OrdinalIgnoreCase));

        // TYPE + TYPE BODY
        Assert.Equal(2, symbols.Count(s => s.Kind == "class" && s.Name == "address_t" && s.Signature != null && s.Signature.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "address_t" && s.Signature != null && s.Signature.Contains("TYPE BODY", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "hr.order_seq");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "customer_v");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "remote_db" && s.Signature != null && s.Signature.Contains("DATABASE LINK", StringComparison.OrdinalIgnoreCase));
        // Oracle allows `CREATE [SHARED] [PUBLIC] DATABASE LINK` — both modifiers may appear together.
        // Oracle は `CREATE [SHARED] [PUBLIC] DATABASE LINK` で両修飾子の 2 語並びも取る。
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "remote_shared_pub_db" && s.Signature != null && s.Signature.Contains("SHARED PUBLIC DATABASE LINK", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "remote_shared_db" && s.Signature != null && s.Signature.Contains("SHARED DATABASE LINK", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "data_dir" && s.Signature != null && s.Signature.StartsWith("CREATE DIRECTORY", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "app_ctx" && s.Signature != null && s.Signature.StartsWith("CREATE CONTEXT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "app_profile" && s.Signature != null && s.Signature.StartsWith("CREATE PROFILE", StringComparison.OrdinalIgnoreCase));

        // Oracle identifiers may contain `$` / `#`
        // Oracle 識別子は `$` / `#` を含められる
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "SYS$ITEMS#1");

        // `BODY` keyword is NOT treated as the object name — these assertions would fail if the
        // generic PACKAGE / TYPE rows absorbed the `BODY` token.
        // `BODY` キーワードは name として取られない — generic な PACKAGE / TYPE 行が `BODY` を
        // 飲み込んでしまうと以下の Assert が失敗する
        Assert.DoesNotContain(symbols, s => s.Name == "BODY");

        // `LINK` keyword must not be eaten by the generic CREATE DATABASE row.
        // `LINK` が generic な CREATE DATABASE 行に食われないこと
        Assert.DoesNotContain(symbols, s => s.Name == "LINK");

        // ALTER counterparts — Oracle body compilation uses `ALTER PACKAGE <name> COMPILE BODY` /
        // `ALTER TYPE <name> COMPILE BODY`, not a `BODY <name>` keyword position.
        // ALTER 側 — Oracle の body コンパイルは `ALTER PACKAGE <name> COMPILE BODY` /
        // `ALTER TYPE <name> COMPILE BODY` であり、`BODY <name>` という位置取りではない。
        Assert.Equal(2, symbols.Count(s => s.Kind == "class" && s.Name == "orders_pkg" && s.Signature != null && s.Signature.StartsWith("ALTER PACKAGE ", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "orders_pkg" && s.Signature != null && s.Signature.Contains("COMPILE BODY", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "address_t" && s.Signature != null && s.Signature.StartsWith("ALTER TYPE ", StringComparison.OrdinalIgnoreCase) && s.Signature.Contains("COMPILE BODY", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "remote_db" && s.Signature != null && s.Signature.StartsWith("ALTER DATABASE LINK", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "data_dir" && s.Signature != null && s.Signature.StartsWith("ALTER DIRECTORY", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "app_profile" && s.Signature != null && s.Signature.StartsWith("ALTER PROFILE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Extract_SQL_KeepsQualifiedNamesWhenDotsHaveSurroundingWhitespace()
    {
        var content =
            "CREATE SCHEMA sales . reporting;\n" +
            "CREATE SCHEMA AUTHORIZATION sales . auth_owner;\n" +
            "CREATE SEQUENCE sales . seq_orders START WITH 1;\n" +
            "CREATE EXTENSION \"sales\" . \"ext_demo\";\n" +
            "CREATE SYNONYM [sales] . [syn_demo] FOR dbo.target;\n" +
            "CREATE DATABASE LINK sales . remote_db CONNECT TO app IDENTIFIED BY 'x' USING 'REMOTE';\n" +
            "CREATE LOGIN sales . app_login WITH PASSWORD = 'x';\n" +
            "CREATE PARTITION FUNCTION sales . pf_orders (int) AS RANGE LEFT FOR VALUES (1);\n" +
            "CREATE PARTITION SCHEME sales . ps_orders AS PARTITION sales . pf_orders ALL TO ([PRIMARY]);\n" +
            "CREATE FULLTEXT CATALOG sales . ft_catalog;\n" +
            "CREATE INDEX sales . idx_users_email ON dbo.Users (Email);\n" +
            "ALTER PARTITION FUNCTION sales . pf_orders() SPLIT RANGE (2);\n" +
            "ALTER SCHEMA sales . reporting TRANSFER dbo.Users;\n" +
            "ALTER EXTENSION \"sales\" . \"ext_demo\" UPDATE TO '2.0';\n" +
            "ALTER DATABASE LINK sales . remote_db;\n" +
            "ALTER SEQUENCE sales . seq_orders RESTART WITH 10;\n" +
            "ALTER SYNONYM [sales] . [syn_demo] FOR dbo.target;\n" +
            "ALTER LOGIN sales . app_login WITH DEFAULT_DATABASE = master;\n" +
            "ALTER INDEX sales . idx_users_email REBUILD;\n" +
            "ALTER PARTITION SCHEME sales . ps_orders NEXT USED [PRIMARY];\n" +
            "ALTER FULLTEXT CATALOG sales . ft_catalog REORGANIZE;\n";
        var symbols = SymbolExtractor.Extract(1, "sql", content);

        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "sales.reporting");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "sales.auth_owner");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "sales.seq_orders");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "\"sales\".\"ext_demo\"");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "[sales].[syn_demo]");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "sales.remote_db");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "sales.app_login");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "sales.pf_orders");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "sales.ps_orders");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "sales.ft_catalog");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "sales.idx_users_email");
    }

    [Fact]
    public void Extract_Terraform_DetectsResources()
    {
        var content =
            "resource \"aws_s3_bucket\" \"my_bucket\" {\n  bucket = \"my-bucket\"\n}\n\n" +
            "provider \"aws\" {\n  region = \"us-east-1\"\n}\n\n" +
            "terraform {\n  required_version = \">= 1.0\"\n}\n\n" +
            "import {\n  id = \"bucket-123\"\n}\n\n" +
            "moved {\n  from = aws_s3_bucket.old_bucket\n  to = aws_s3_bucket.my_bucket\n}\n\n" +
            "removed {\n  from = aws_s3_bucket.deprecated_bucket\n}\n\n" +
            "check \"health\" {\n  assert {\n    condition = true\n  }\n}\n\n" +
            "locals {\n  region = \"us-east-1\"\n}\n\n" +
            "variable \"region\" {\n  default = \"us-east-1\"\n}\n\n" +
            "output \"bucket_arn\" {\n  value = aws_s3_bucket.my_bucket.arn\n}\n\n" +
            "module \"vpc\" {\n  source = \"./modules/vpc\"\n}";
        var symbols = SymbolExtractor.Extract(1, "terraform", content);

        // resource captures logical name (second quoted token), not provider type
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "my_bucket");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "aws");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "terraform");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "import");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "moved");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "removed");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "health");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "locals");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "region");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "bucket_arn");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "vpc");
    }

    [Fact]
    public void Extract_PHP_DetectsExpandedFeatures()
    {
        var content = """
            <?php
            namespace App\Models;

            use Illuminate\Database\Eloquent\Model;

            readonly class Config {
                public const VERSION = '1.0';
                public function getName(): string { return ''; }
            }

            enum Status: string {
                case Active = 'active';
                case Pending = 'pending';
            }

            enum OrderStatus {
                case Draft;
                case Published;
            }

            enum Priority: int {
                case Low = 1;

                public function label(): string {
                    return match ($this) {
                        Priority::Low => 'low',
                    };
                }
            }

            function inspectState(int $state): void {
                switch ($state) {
                    case 1:
                        break;
                    case 'x':
                        break;
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "php", content);

        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name.Contains("App"));
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name.Contains("Model"));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "VERSION");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "getName");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Status");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "OrderStatus");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Priority");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Active" && s.ContainerName == "Status" && s.ReturnType == "'active'");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Pending" && s.ContainerName == "Status" && s.ReturnType == "'pending'");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Draft" && s.ContainerName == "OrderStatus" && s.ReturnType == null);
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Published" && s.ContainerName == "OrderStatus" && s.ReturnType == null);
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Low" && s.ContainerName == "Priority" && s.ReturnType == "1");
        Assert.Equal(5, symbols.Count(s => s.Kind == "property"));
    }

    [Fact]
    public void Extract_PHP_DetectsImportAliasesGroupUseAndLiteralRequires()
    {
        var content = """
            <?php

            use Closure;
            use Illuminate\Auth\Middleware\Authenticate;
            use Illuminate\Support\Arr as A;
            use function Laravel\Prompts\text;
            use const Foo\Bar\BAZ;
            use X\Y\{A, B as C, D};

            require 'static/path.php';
            require __DIR__ . '/bootstrap.php';
            require $variable;
            """;

        var symbols = SymbolExtractor.Extract(1, "php", content);
        var imports = symbols.Where(s => s.Kind == "import").ToList();

        Assert.Equal(9, imports.Count);
        Assert.Contains(imports, s => s.Name == "Closure");
        Assert.Contains(imports, s => s.Name == "Illuminate\\Auth\\Middleware\\Authenticate");
        Assert.Contains(imports, s => s.Name == "A");
        Assert.Contains(imports, s => s.Name == "Laravel\\Prompts\\text");
        Assert.Contains(imports, s => s.Name == "Foo\\Bar\\BAZ");
        Assert.Contains(imports, s => s.Name == "X\\Y\\A");
        Assert.Contains(imports, s => s.Name == "C");
        Assert.Contains(imports, s => s.Name == "X\\Y\\D");
        Assert.Contains(imports, s => s.Name == "static/path.php");
        Assert.DoesNotContain(imports, s => s.Name.Contains("__DIR__", StringComparison.Ordinal));
        Assert.DoesNotContain(imports, s => s.Name.Contains("$variable", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_Swift_DetectsActorAndTypealias()
    {
        var content = "public actor NetworkManager {\n    func fetch() { }\n}\n\npublic typealias Handler = (Data) -> Void\n\ntypealias UserID = Int\npublic typealias Callback = (Int) -> Int\n\ndistributed actor RemoteWorker { }";
        var symbols = SymbolExtractor.Extract(1, "swift", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "NetworkManager");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "fetch");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Handler");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "UserID");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Callback");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "RemoteWorker");
    }

    [Fact]
    public void Extract_Swift_DetectsEnumCasesAndIndirectEnums()
    {
        var content = """
            public enum NetworkError: Error {
                case timeout
                case server(code: Int, message: String)
                case client(Int)
                case unknown
            }

            indirect enum Tree<T> {
                case leaf(T)
                indirect case node(Tree<T>, Tree<T>)
            }

            enum Status: String, Codable {
                case active
                case inactive = "off"
                case pending
            }

            func handle(_ error: NetworkError) {
                switch error {
                case .overheated:
                    break
                case let .recoverable(code, message):
                    break
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "swift", content);

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "NetworkError");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Tree");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Status");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "timeout");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "server");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "client");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "unknown");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "leaf");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "node");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "active");

        var inactive = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "inactive"));
        Assert.Equal("\"off\"", inactive.ReturnType);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "pending");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "overheated");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "recoverable");
    }

    [Fact]
    public void Extract_ObjC_DetectsInterfacesPropertiesMethodsAndImports()
    {
        var content = """
            #import <Foundation/Foundation.h>

            @interface Dog : NSObject
            @property (nonatomic, strong) NSString *name;
            @property (nonatomic, assign) NSInteger age;
            - (void)bark;
            - (NSString *)greet:(NSString *)greeting withName:(NSString *)name;
            + (instancetype)dogWithName:(NSString *)name;
            @end

            @protocol Animal <NSObject>
            - (void)move;
            @optional
            - (NSString *)describe;
            @end

            @implementation Dog
            - (void)bark {
                NSLog(@"Woof!");
            }
            @end
            """;
        var symbols = SymbolExtractor.Extract(1, "objc", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Dog");
        Assert.Equal(2, symbols.Count(s => s.Kind == "class" && s.Name == "Dog"));
        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "Animal");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "name");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "age");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "bark");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "greet");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "dogWithName");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "move");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "describe");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Foundation/Foundation.h");
    }

    [Fact]
    public void Extract_Ruby_DetectsAttrAndRailsDSL()
    {
        var content = "class User < ActiveRecord::Base\n  attr_accessor :name\n  attr_reader :email\n  has_many :posts\n  belongs_to :company\n  scope :active\n\n  def initialize(name)\n    @name = name\n  end\nend";
        var symbols = SymbolExtractor.Extract(1, "ruby", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "name");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "email");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "posts");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "company");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "active");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "initialize");
    }

    [Fact]
    public void Extract_Ruby_DetectsAttrListsAndAliases()
    {
        var content = """
            class Person
              attr_accessor :name, :age, :email
              attr_reader :id, :created_at
              attr_writer :internal_flag, :debug_count
              attr_accessor :nickname

              def greet(greeting = "Hello")
                "#{greeting}, #{name}"
              end

              alias_method :full_name, :name
              alias_method :profile!, :name
              alias greet_alias greet
              alias shout! greet
              alias :display_name :name
            end
            """;
        var symbols = SymbolExtractor.Extract(1, "ruby", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Person");
        Assert.Equal(1, symbols.Count(s => s.Kind == "property" && s.Name == "name"));
        Assert.Equal(1, symbols.Count(s => s.Kind == "property" && s.Name == "age"));
        Assert.Equal(1, symbols.Count(s => s.Kind == "property" && s.Name == "email"));
        Assert.Equal(1, symbols.Count(s => s.Kind == "property" && s.Name == "id"));
        Assert.Equal(1, symbols.Count(s => s.Kind == "property" && s.Name == "created_at"));
        Assert.Equal(1, symbols.Count(s => s.Kind == "property" && s.Name == "internal_flag"));
        Assert.Equal(1, symbols.Count(s => s.Kind == "property" && s.Name == "debug_count"));
        Assert.Equal(1, symbols.Count(s => s.Kind == "property" && s.Name == "nickname"));
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "greet");
        Assert.Equal(1, symbols.Count(s => s.Kind == "function" && s.Name == "full_name"));
        Assert.Equal(1, symbols.Count(s => s.Kind == "function" && s.Name == "profile!"));
        Assert.Equal(1, symbols.Count(s => s.Kind == "function" && s.Name == "greet_alias"));
        Assert.Equal(1, symbols.Count(s => s.Kind == "function" && s.Name == "shout!"));
        Assert.Equal(1, symbols.Count(s => s.Kind == "function" && s.Name == "display_name"));
    }

    [Fact]
    public void Extract_Rust_DetectsExpandedFeatures()
    {
        var content = "macro_rules! my_macro {\n    () => {};\n}\n\npub mod utils {\n}\n\nconst MAX_SIZE: usize = 1024;\nstatic COUNTER: AtomicU32 = AtomicU32::new(0);\npub const fn default_value() -> i32 { 42 }\npub unsafe fn raw_ptr() { }\ntype Result<T> = std::result::Result<T, Error>;\ntrait Iter {\n    type Item;\n    fn next(&mut self) -> Option<Self::Item>;\n}\ntype Callback = fn(i32) -> i32;\npub union MyUnion { f: f32 }";
        var symbols = SymbolExtractor.Extract(1, "rust", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "my_macro");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "utils");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MAX_SIZE");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "COUNTER");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "default_value");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "raw_ptr");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Result");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Item");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Callback");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "MyUnion");
    }

    [Fact]
    public void Extract_Rust_MapsImplBlocksToImplementingType()
    {
        var content = """
            struct Widget;
            struct Task<T> { value: T }
            struct Empty<T> { value: T }

            impl Widget {}
            impl Debug for Widget {}
            impl<T> Future for Task<T> {}
            unsafe impl<T> Send for Empty<T> {}
            """;
        var symbols = SymbolExtractor.Extract(1, "rust", content);

        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Widget");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Task");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Empty");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Widget");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Task");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Empty");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Debug");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Future");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Send");
    }

    [Fact]
    public void Extract_Go_DetectsTypeAliasAndConst()
    {
        var content = "type Handler struct {\n}\ntype ID = string\ntype Callback func(int) int\ntype Logger interface {\n}\n\nconst (\n    MaxRetries = 3\n    DefaultTimeout = 30\n)\n\nvar GlobalConfig Config";
        var symbols = SymbolExtractor.Extract(1, "go", content);

        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Handler");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "ID");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Callback");
        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "Logger");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "MaxRetries");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "DefaultTimeout");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "GlobalConfig");
    }

    [Fact]
    public void Extract_Rust_DetectsFunctionsAndStructs()
    {
        var content = "pub fn handle_request() {}\npub struct Config {}\nimpl Config {";
        var symbols = SymbolExtractor.Extract(1, "rust", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "handle_request");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Config");
    }

    [Fact]
    public void Extract_Rust_DetectsEnumVariants()
    {
        var content = """
            pub enum Shape {
                Circle { radius: f64 },
                Rectangle(f64, f64),
                Point,
            }

            pub enum Result<T, E> {
                Ok(T),
                Err(E),
            }

            pub enum Color {
                Red,
                Green,
                Blue,
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "rust", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Circle");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Rectangle");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Point");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Ok");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Err");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Red");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Green");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Blue");
        Assert.Equal(8, symbols.Count(s => s.Kind == "property"));
    }

    [Fact]
    public void Extract_Rust_LifetimeAnnotationsDoNotBreakBraceRanges()
    {
        var content = """
            pub struct Holder<'a> {
                value: &'a str,
            }

            impl<'a> Holder<'a> {
                pub fn get(&self) -> &'a str {
                    self.value
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "rust", content);

        var holder = Assert.Single(symbols.Where(s => s.Kind == "struct" && s.Name == "Holder"));
        var get = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "get"));

        Assert.Equal(3, holder.EndLine);
        Assert.Equal(8, get.EndLine);
        Assert.Equal("class", get.ContainerKind);
        Assert.Equal("Holder", get.ContainerName);
    }

    [Fact]
    public void Extract_UnknownLang_ReturnsEmpty()
    {
        // Unsupported languages return no symbols
        // 未サポート言語は空を返す
        var symbols = SymbolExtractor.Extract(1, "markdown", "# Heading");
        Assert.Empty(symbols);
    }

    [Fact]
    public void Extract_NullLang_ReturnsEmpty()
    {
        var symbols = SymbolExtractor.Extract(1, null, "some content");
        Assert.Empty(symbols);
    }

    [Fact]
    public void Extract_LineNumbers_AreOneBased()
    {
        // Line numbers should be 1-based
        // 行番号は1始まりであること
        var content = "x = 1\ndef foo():\n    pass";
        var symbols = SymbolExtractor.Extract(1, "python", content);

        Assert.Single(symbols);
        Assert.Equal(2, symbols[0].Line);
    }

    [Fact]
    public void Extract_Java_DetectsClassesAndMethods()
    {
        // Java: class, interface, methods / Java: クラス、インターフェース、メソッド
        var content = "public class UserService {\n    public User getUser(int id) {\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "getUser");
    }

    [Fact]
    public void Extract_Java_DetectsGenericMethodsWithTypeParameterPrefix()
    {
        var content = """
            package com.example;

            public class GenericService {
                public <T> T first(java.util.List<T> items) { return items.get(0); }
                public <K, V> V get(java.util.Map<K, V> map, K key) { return map.get(key); }
                public <T extends Comparable<T>> T max(T a, T b) { return a.compareTo(b) >= 0 ? a : b; }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "GenericService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first" && s.ContainerName == "GenericService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "get" && s.ContainerName == "GenericService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "max" && s.ContainerName == "GenericService");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "return");
    }

    [Fact]
    public void Extract_Java_DetectsRecordAndSealedClass()
    {
        // Java 16+ record, Java 17+ sealed class / Java 16 の record、Java 17 の sealed class
        var content = "public record Point(int x, int y) { }\npublic sealed class Shape permits Circle, Rect { }";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Point");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Shape");
    }

    [Fact]
    public void Extract_Java_DetectsRecordPrimaryComponentsAsProperties()
    {
        var content = """
            package com.example;

            public record Point(int x, int y) {}
            public record Range(
                int low,
                int high
            ) {
                public int span() { return high - low; }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        var pointX = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "x" && s.ContainerName == "Point"));
        Assert.Equal("int", pointX.ReturnType);
        Assert.Equal("class", pointX.ContainerKind);
        Assert.Equal("Point", pointX.ContainerQualifiedName);
        Assert.Equal("public", pointX.Visibility);

        var rangeLow = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "low" && s.ContainerName == "Range"));
        Assert.Equal("int", rangeLow.ReturnType);
        Assert.Equal(5, rangeLow.Line);

        var rangeHigh = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "high" && s.ContainerName == "Range"));
        Assert.Equal("int", rangeHigh.ReturnType);
        Assert.Equal(6, rangeHigh.Line);

        var span = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "span"));
        Assert.Equal("class", span.ContainerKind);
        Assert.Equal("Range", span.ContainerName);
        Assert.Equal("Range", span.ContainerQualifiedName);
    }

    [Fact]
    public void Extract_Java_DetectsLongAndCommentedRecordPrimaryComponentsAsProperties()
    {
        var componentLines = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"    int p{i},"));
        var content = $$"""
            package com.example;

            public record Big(
            {{componentLines}}
                int tail
            ) {}

            public record Point(
                int x /* separator, comment */,
                // the next component must still parse
                int y
            ) {}
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "p1" && s.ContainerName == "Big");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "p30" && s.ContainerName == "Big");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "tail" && s.ContainerName == "Big");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "x" && s.ContainerName == "Point");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "y" && s.ContainerName == "Point");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "separator" && s.ContainerName == "Point");

        var tail = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "tail" && s.ContainerName == "Big"));
        Assert.Equal(34, tail.Line);

        var pointY = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "y" && s.ContainerName == "Point"));
        Assert.Equal(40, pointY.Line);
    }

    [Fact]
    public void Extract_Java_DetectsRecordPrimaryComponentsWithSpacedGenericTypes()
    {
        var content = """
            import java.util.Map;

            public record Sample(Map <String, Integer> values, int count) {}
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        var values = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "values" && s.ContainerName == "Sample"));
        Assert.Equal("Map <String, Integer>", values.ReturnType);

        var count = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "count" && s.ContainerName == "Sample"));
        Assert.Equal("int", count.ReturnType);
    }

    [Fact]
    public void Extract_Java_TracksRecordPrimaryComponentLineAfterAnnotations()
    {
        var content = """
            public record Person(
                @Deprecated
                String name,
                @Deprecated
                int age
            ) {}
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        var name = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "name" && s.ContainerName == "Person"));
        Assert.Equal(3, name.Line);

        var age = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "age" && s.ContainerName == "Person"));
        Assert.Equal(5, age.Line);
    }

    [Fact]
    public void Extract_Java_RecordComponents_DoNotDisruptBodyMembers()
    {
        var content = """
            package repro;

            public record Point(int x) {
                public int doubled() { return x * 2; }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        var doubled = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "doubled"));
        Assert.Equal("class", doubled.ContainerKind);
        Assert.Equal("Point", doubled.ContainerName);
        Assert.Equal("Point", doubled.ContainerQualifiedName);
    }

    [Fact]
    public void Extract_Java_DetectsSameLineAnnotatedMethodsCompactConstructorsAndEnumConstantOverrides()
    {
        var content = """
            package com.example;

            public class Same {
                @Override public String toString() { return "x"; }
                @Deprecated public int legacy() { return 0; }

                @Override
                public int hashCode() { return 42; }
            }

            public enum Op {
                ADD {
                    @Override public int apply(int a, int b) { return a + b; }
                },
                SUB {
                    @Override public int apply(int a, int b) { return a - b; }
                };
                public abstract int apply(int a, int b);
            }

            public record Range(int low, int high) {
                public Range {
                    if (low > high) throw new IllegalArgumentException();
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        var toString = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "toString"));
        Assert.Equal("class", toString.ContainerKind);
        Assert.Equal("Same", toString.ContainerName);

        var legacy = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "legacy"));
        Assert.Equal("class", legacy.ContainerKind);
        Assert.Equal("Same", legacy.ContainerName);

        var addApply = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "apply" && s.ContainerName == "ADD"));
        Assert.Equal("function", addApply.ContainerKind);
        Assert.Equal("Op.ADD", addApply.ContainerQualifiedName);

        var subApply = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "apply" && s.ContainerName == "SUB"));
        Assert.Equal("function", subApply.ContainerKind);
        Assert.Equal("Op.SUB", subApply.ContainerQualifiedName);

        var abstractApply = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "apply" && s.ContainerName == "Op"));
        Assert.Equal("enum", abstractApply.ContainerKind);

        var compactCtor = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Range"));
        Assert.Equal("class", compactCtor.ContainerKind);
        Assert.Equal("Range", compactCtor.ContainerName);
        Assert.NotNull(compactCtor.BodyStartLine);
        Assert.NotNull(compactCtor.BodyEndLine);
    }

    [Fact]
    public void Extract_Java_DetectsAllmanStyleCompactConstructors()
    {
        var content = """
            public record Range(int low, int high) {
                public Range
                {
                    if (low > high) throw new IllegalArgumentException();
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        var compactCtor = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Range"));
        Assert.Equal("class", compactCtor.ContainerKind);
        Assert.Equal("Range", compactCtor.ContainerName);
        Assert.Equal(2, compactCtor.StartLine);
        Assert.NotNull(compactCtor.BodyStartLine);
        Assert.NotNull(compactCtor.BodyEndLine);
        Assert.True(compactCtor.BodyStartLine >= compactCtor.StartLine);
    }

    [Fact]
    public void Extract_Java_SwitchExpressionsDoNotEmitPhantomMethods()
    {
        var content = """
            package com.example;

            sealed interface Shape permits Circle, Square {}
            record Circle(double r) implements Shape {}
            record Square(double side) implements Shape {}

            public class Matcher {
                public double area(Shape shape) {
                    return switch (shape) {
                        case Circle(double r) -> Math.PI * r * r;
                        case Square(double side) -> side * side;
                        default -> 0.0;
                    };
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Circle");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Square");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "area" && s.ContainerName == "Matcher");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "switch");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Circle");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Square");
    }

    [Fact]
    public void Extract_Java_DetectsSameLineAnnotatedDeclarationsWhenAnnotationArgumentsContainParen()
    {
        var content = """
            public class Demo {
                @Label(")") public int broken() { return 1; }
            }

            @Ann(value = helper(")"))
            public record Wrapped(int value) {}
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "broken" && s.ContainerKind == "class" && s.ContainerName == "Demo");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Wrapped");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "value" && s.ContainerKind == "class" && s.ContainerName == "Wrapped");
    }

    [Fact]
    public void Extract_Java_DetectsSameLineAnnotatedMethodsWhenAnnotationArgumentsContainBraceLiterals()
    {
        var content = """
            public class Demo {
                @SuppressWarnings({"unchecked"}) public int first() { return 1; } int second() { return 2; }
            }

            public class Solo {
                @SuppressWarnings({"rawtypes"}) public int only() { return 3; }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first" && s.ContainerKind == "class" && s.ContainerName == "Demo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second" && s.ContainerKind == "class" && s.ContainerName == "Demo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "only" && s.ContainerKind == "class" && s.ContainerName == "Solo");
    }

    [Fact]
    public void Extract_Java_DetectsBraceLiteralAnnotationsOnSameLineTypesAndEnumBodies()
    {
        var content = """
            @Target({ElementType.TYPE}) public record Wrapped(int value) { public int twice() { return value * 2; } }

            public enum Op {
                ADD { @SuppressWarnings({"unchecked"}) public int apply(int a, int b) { return a + b; } };
                public abstract int apply(int a, int b);
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Wrapped");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "value" && s.ContainerKind == "class" && s.ContainerName == "Wrapped");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "twice" && s.ContainerKind == "class" && s.ContainerName == "Wrapped");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "apply" && s.ContainerKind == "function" && s.ContainerName == "ADD");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "apply" && s.ContainerKind == "enum" && s.ContainerName == "Op");
    }

    [Fact]
    public void Extract_Java_SameLineRecordsDoNotEmitPhantomHeaderFunctions()
    {
        var content = """
            public record Empty(int x) {}
            public record Inline(int x) { public int twice() { return x * 2; } }
            public record Compact(int x) { public Compact { if (x < 0) throw new IllegalArgumentException(); } }
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Empty");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Inline");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Compact");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "x" && s.ContainerName == "Empty");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "twice" && s.ContainerKind == "class" && s.ContainerName == "Inline");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Compact" && s.ContainerKind == "class" && s.ContainerName == "Compact");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name is "Empty" or "Inline" && s.ReturnType == "record");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Compact" && s.ReturnType == "record");
    }

    [Fact]
    public void Extract_Java_SameLineCompactConstructorAfterSiblingStillIndexesConstructor()
    {
        var content = """
            public record R(int x) { int first() { return x; } public R { if (x < 0) throw new IllegalArgumentException(); } }
            public record Annotated(int x) { @Deprecated int first() { return x; } @Deprecated public Annotated { if (x < 0) throw new IllegalArgumentException(); } }
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "R");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "x" && s.ContainerKind == "class" && s.ContainerName == "R");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first" && s.ContainerKind == "class" && s.ContainerName == "R");
        var compactCtor = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "R"));
        Assert.Equal("class", compactCtor.ContainerKind);
        Assert.Equal("R", compactCtor.ContainerName);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first" && s.ContainerKind == "class" && s.ContainerName == "Annotated");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Annotated" && s.ContainerKind == "class" && s.ContainerName == "Annotated");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name is "R" or "Annotated" && s.ReturnType == "record");
    }

    [Fact]
    public void Extract_Java_SameLineMembersAfterCompactConstructorStillIndex()
    {
        var content = """
            public record R(int x) { public R { } int later() { return x; } }
            public record Annotated(int x) { @Deprecated public Annotated { } @Deprecated int later() { return x; } }
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "R" && s.ContainerKind == "class" && s.ContainerName == "R");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "later" && s.ContainerKind == "class" && s.ContainerName == "R");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Annotated" && s.ContainerKind == "class" && s.ContainerName == "Annotated");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "later" && s.ContainerKind == "class" && s.ContainerName == "Annotated");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.ReturnType == "record");
    }

    [Fact]
    public void Extract_Java_HandlesSameLineSiblingMethodsInsideEnumBody()
    {
        var content = """
            public enum Demo {
                A;
                int first() { return 1; } int second() { return 2; }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        var first = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "first" && s.ContainerKind == "enum" && s.ContainerName == "Demo"));
        var second = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "second" && s.ContainerKind == "enum" && s.ContainerName == "Demo"));
        Assert.Equal("int first() { return 1; }", first.Signature);
        Assert.Equal("int second() { return 2; }", second.Signature);
    }

    [Fact]
    public void Extract_Java_DetectsStaticFinalAndEnumMembers()
    {
        var content = "public class Config {\n    public static final String VERSION = \"1.0\";\n    private static final int MAX_RETRIES = 3;\n}\n\npublic enum Status {\n    ACTIVE,\n    INACTIVE,\n    PENDING;\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "VERSION");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MAX_RETRIES");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Status");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ACTIVE");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "INACTIVE");
    }

    [Fact]
    public void Extract_Java_DetectsAnnotationType()
    {
        var content = "public @interface MyAnnotation {\n    String value();\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "MyAnnotation");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "value");
    }

    [Fact]
    public void Extract_Java_DetectsModuleInfoDeclarationAndDirectives()
    {
        const string content = """
            open module com.example.app {
                requires static transitive java.logging;
                requires java.base;
                exports com.example.api;
                exports com.example.internal to com.example.plugin, com.example.tools;
                opens com.example.model;
                uses com.example.spi.MyService;
                provides com.example.spi.MyService with com.example.impl.DefaultService, com.example.impl.BackupService;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        var module = Assert.Single(symbols.Where(s => s.Kind == "namespace" && s.Name == "com.example.app"));
        Assert.Equal(1, module.Line);
        Assert.Equal(1, module.StartLine);
        Assert.Equal(9, module.EndLine);

        var imports = symbols.Where(s => s.Kind == "import").ToList();
        Assert.Equal(7, imports.Count);
        Assert.Contains(imports, s => s.Name == "java.logging" && s.ContainerName == "com.example.app");
        Assert.Contains(imports, s => s.Name == "java.base" && s.ContainerName == "com.example.app");
        Assert.Contains(imports, s => s.Name == "com.example.api" && s.ContainerName == "com.example.app");
        Assert.Contains(imports, s => s.Name == "com.example.internal" && s.ContainerName == "com.example.app");
        Assert.Contains(imports, s => s.Name == "com.example.model" && s.ContainerName == "com.example.app");
        Assert.Equal(2, imports.Count(s => s.Name == "com.example.spi.MyService" && s.ContainerName == "com.example.app"));
    }

    [Fact]
    public void Extract_Java_DetectsModuleInfoDeclarationWithAllmanBrace()
    {
        const string content = """
            module com.example.app
            {
                requires java.base;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        var module = Assert.Single(symbols.Where(s => s.Kind == "namespace" && s.Name == "com.example.app"));
        Assert.Equal(1, module.Line);
        Assert.Equal(1, module.StartLine);
        Assert.Equal(4, module.EndLine);
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "java.base" && s.ContainerName == "com.example.app");
    }

    [Fact]
    public void Extract_Java_DetectsModuleInfoDirectivesOnAllmanBraceLine()
    {
        const string content = """
            module com.example.app
            { requires java.base;
              exports com.example.api;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        var imports = symbols.Where(s => s.Kind == "import").ToList();
        Assert.Equal(2, imports.Count);
        Assert.Contains(imports, s => s.Name == "java.base" && s.ContainerName == "com.example.app");
        Assert.Contains(imports, s => s.Name == "com.example.api" && s.ContainerName == "com.example.app");
    }

    [Fact]
    public void Extract_Java_DetectsModuleInfoDirectivesWithMultilineListsAndComments()
    {
        const string content = """
            module com.example.app {
                requires /*comment*/ java.base;
                exports com.example.internal
                    to com.example.plugin,
                       com.example.tools;
                opens com.example.model
                    to com.example.viewer,
                       com.example.editor;
                uses com.example.spi.MyService;
                provides com.example.spi.MyService
                    with com.example.impl.DefaultService,
                         com.example.impl.BackupService;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        var imports = symbols.Where(s => s.Kind == "import").ToList();
        Assert.Equal(5, imports.Count);
        Assert.Contains(imports, s => s.Name == "java.base");
        Assert.Contains(imports, s => s.Name == "com.example.internal");
        Assert.Contains(imports, s => s.Name == "com.example.model");
        Assert.Equal(2, imports.Count(s => s.Name == "com.example.spi.MyService"));
        Assert.Contains(imports, s => s.Signature == "requires /*comment*/ java.base;");
        Assert.Contains(imports, s => s.Signature == "exports com.example.internal to com.example.plugin, com.example.tools;");
        Assert.Contains(imports, s => s.Signature == "opens com.example.model to com.example.viewer, com.example.editor;");
        Assert.Contains(imports, s => s.Signature == "provides com.example.spi.MyService with com.example.impl.DefaultService, com.example.impl.BackupService;");
    }

    [Fact]
    public void Extract_Java_DetectsFlexibleConstantOrder()
    {
        // final static order (reversed) and generic types with spaces
        var content = "public class Config {\n    private final static int MAX = 100;\n    public static final Map<String, Integer> COUNTS = Map.of();\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MAX");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "COUNTS");
    }

    [Fact]
    public void Extract_Java_DetectsEnumMembersWithTwoSpaceIndent()
    {
        // 2-space indent enum / 2スペースインデントの enum
        var content = "public enum Color {\n  RED,\n  GREEN,\n  BLUE;\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "RED");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GREEN");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "BLUE");
    }

    [Fact]
    public void Extract_Java_DetectsTabIndentedEnumMembers()
    {
        // Single-tab indent enum (EditorConfig indent_style=tab) — regression for #364 Java side.
        // タブ1文字でインデントされた enum（#364 の Java 側クロス言語修正のリグレッション）。
        var content = "public enum Color {\n\tRED,\n\tGREEN,\n\tBLUE;\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "RED");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GREEN");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "BLUE");
    }

    [Fact]
    public void Extract_Java_DoesNotExtractMethodCallsAsEnumMembers()
    {
        // A class body method call like `\tRED();` must not be misread as an enum member — regression for #292.
        // クラス本体内のメソッド呼び出し `\tRED();` を enum メンバーとして誤検出しないこと（#292 のリグレッション）。
        var content = "public class Test {\n\tvoid run() {\n\t\tRED();\n\t\tGREEN();\n\t}\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "RED");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "GREEN");
    }

    [Fact]
    public void Extract_Java_HandlesAnnotationWithQuotedParen()
    {
        // `@Label(")")` must not fool the paren-balance counter — the `)` is inside a string literal.
        // `@Label(")")` の `)` は文字列内なので括弧バランスで閉じてはいけない。
        var content = "public enum E {\n    @Label(\")\") A,\n    B;\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "A" && s.ContainerName == "E");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "B" && s.ContainerName == "E");
    }

    [Fact]
    public void Extract_Java_HandlesAnnotationMemberDefaultArrayValueAsBodyLess()
    {
        // `default { ... }` on an annotation member is part of the default value, not a real
        // member body. The scanner must keep the declaration body-less so later same-line
        // siblings still survive.
        // annotation member の `default { ... }` は本体ではなく default 値の一部。
        // body-less のまま保持し、同一行の後続 sibling を落とさないこと。
        var content = "@interface Tags { String[] value() default {\"a\", \"b\"}; int age(); } class C {}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        var value = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "value" && s.ContainerName == "Tags"));
        Assert.Equal("String[] value() default {\"a\", \"b\"};", value.Signature);
        Assert.Null(value.BodyStartLine);
        Assert.Null(value.BodyEndLine);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "age" && s.ContainerName == "Tags" && s.BodyStartLine == null);
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "C");
    }

    [Fact]
    public void Extract_Java_HandlesBlockCommentBetweenAnnotationAndMember()
    {
        // Block comments between `@Annotation` and the member name must be skipped.
        // アノテーションとメンバー名の間の block comment を読み飛ばすこと。
        var content = "public enum E {\n    @A /*note*/ B,\n    C;\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "B" && s.ContainerName == "E");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "C" && s.ContainerName == "E");
    }

    [Fact]
    public void Extract_Java_HandlesEmptyEnumBody()
    {
        // `enum X {}` has no members; the enum itself should still be extracted.
        // 空本体の enum でも enum 自身は抽出されること。
        var content = "public enum Empty {}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Empty");
        Assert.DoesNotContain(symbols, s => s.ContainerKind == "enum" && s.ContainerName == "Empty");
    }

    [Fact]
    public void Extract_Java_HandlesEnumWithOnlySemicolon()
    {
        // `enum X { ; }` declares no members but may still hold methods/fields.
        // 本体が `;` のみの enum はメンバーを持たない。
        var content = "public enum NoMembers {\n    ;\n    private int count;\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "NoMembers");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.ContainerKind == "enum" && s.ContainerName == "NoMembers" && s.BodyStartLine == null);
    }

    [Fact]
    public void Extract_Java_HandlesTextBlockContainingBrace()
    {
        // A `}` inside a Java text block (""") must not close the enum body prematurely, which
        // would otherwise drop every member after the text block. Regression for FindJavaBraceRange.
        // Java text block 内の `}` で enum 本体範囲を誤って閉じず、後続メンバーが落ちないこと。
        var content = "public enum TxtBlock {\n  FIRST(\"\"\"\n    end }\n    more ;\n    \"\"\"),\n  SECOND;\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "FIRST" && s.ContainerName == "TxtBlock");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "SECOND" && s.ContainerName == "TxtBlock");
    }

    [Fact]
    public void Extract_Java_HandlesStringContainingBrace()
    {
        // A `}` inside a regular string literal must not close the enum body prematurely either.
        // 文字列リテラル内の `}` でも enum 本体範囲を閉じないこと。
        var content = "public enum QuotedBrace {\n    A(\"text with } inside\"),\n    B;\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "A" && s.ContainerName == "QuotedBrace");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "B" && s.ContainerName == "QuotedBrace");
    }

    [Fact]
    public void Extract_Java_DetectsUnicodeEnumMembers()
    {
        var content = "public enum Localized {\n    RÉSUMÉ,\n    NAÏVE;\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "RÉSUMÉ" && s.ContainerName == "Localized");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "NAÏVE" && s.ContainerName == "Localized");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "R" && s.ContainerName == "Localized");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "NA" && s.ContainerName == "Localized");
    }

    [Fact]
    public void Extract_Java_HandlesTrailingComma()
    {
        // `enum X { A, B, }` — trailing comma before closing brace must not emit an empty member.
        // `,` の直後が body end でも空メンバーを出さないこと。
        var content = "public enum Trailing {\n    A,\n    B,\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "A" && s.ContainerName == "Trailing");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "B" && s.ContainerName == "Trailing");
        Assert.Equal(2, symbols.Count(s => s.ContainerKind == "enum" && s.ContainerName == "Trailing" && s.BodyStartLine == null));
    }

    [Fact]
    public void Extract_Java_HandlesAnonymousMemberBody()
    {
        // Anonymous member bodies (`A { void f() {} }`) must not suppress the following member.
        // 匿名メンバー本体があっても直後のメンバーが消えないこと。
        var content = "public enum WithBody {\n    A {\n        void f() {}\n    },\n    B;\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "A" && s.ContainerName == "WithBody" && s.BodyStartLine != null);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "f" && s.ContainerKind == "function" && s.ContainerName == "A");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "B" && s.ContainerName == "WithBody" && s.BodyStartLine == null);
    }

    [Fact]
    public void Extract_Java_HandlesSameLineAnonymousMemberBodyMethods()
    {
        var content = """
            public enum Mix {
                A { @Override public int f() { return 1; } int g() { return 2; } },
                B { @Override public int f() { return 3; } int h() { return 4; } };
                public abstract int f();
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "A" && s.ContainerKind == "enum" && s.ContainerName == "Mix" && s.BodyStartLine != null);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "B" && s.ContainerKind == "enum" && s.ContainerName == "Mix" && s.BodyStartLine != null);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "f" && s.ContainerKind == "function" && s.ContainerName == "A");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "g" && s.ContainerKind == "function" && s.ContainerName == "A");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "f" && s.ContainerKind == "function" && s.ContainerName == "B");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "h" && s.ContainerKind == "function" && s.ContainerName == "B");
    }

    [Fact]
    public void Extract_Java_RecoversMembersWhenAnnotationIsMalformed()
    {
        // An unclosed `@Ann(` would otherwise make the primary scanner swallow subsequent member lines.
        // The per-line fallback rescues obvious uppercase-identifier members.
        // 未閉鎖の `@Ann(` で primary scanner が後続行を飲み込んでしまう状況を、line fallback で救済する。
        var content = "public enum E {\n    @Ann(\n    A,\n    B;\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "A" && s.ContainerName == "E");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "B" && s.ContainerName == "E");
    }

    [Fact]
    public void Extract_Java_RecoveryIgnoresLinesInsideAnonymousMemberBody()
    {
        // Malformed input forces the recovery pass; recovery must track brace depth so uppercase
        // call statements inside an anonymous member body (e.g. `ACTIVATE_HELPER();`) are not
        // emitted as phantom enum members, and the subsequent real member is still captured.
        // 不整形入力で recovery が走るとき、匿名メンバー本体内の大文字呼び出しを誤って member にせず、
        // その後の実メンバーを救済できること。
        var content = "public enum E {\n    @Bad(\n    RED {\n        void f() { ACTIVATE_HELPER(); }\n    },\n    GREEN;\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GREEN" && s.ContainerName == "E" && s.BodyStartLine == null);
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "ACTIVATE_HELPER" && s.ContainerName == "E" && s.BodyStartLine == null);
    }

    [Fact]
    public void Extract_Java_RecoveryDedupsByNameAcrossAnnotationStartLines()
    {
        // Primary scanner stamps StartLine at the annotation line; recovery stamps the member-name
        // line. StartLine-based dedup would double-emit. Name-based dedup must suppress duplicates.
        // primary scanner と recovery で StartLine 基準が異なるため、名前基準で重複排除されること。
        var content = "public enum E {\n    @Marker\n    A,\n    @Bad(\n    B;\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        var aMembers = symbols.Where(s => s.Kind == "function" && s.Name == "A" && s.ContainerName == "E" && s.BodyStartLine == null).ToList();
        Assert.Single(aMembers);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "B" && s.ContainerName == "E" && s.BodyStartLine == null);
    }

    [Fact]
    public void Extract_Java_StopsEnumMembersAtSemicolon()
    {
        // After the first top-level `;` inside the enum body, non-member declarations must not be captured as members.
        // Enum members have no body range (BodyStartLine == null); the method extractor populates a body range.
        // enum 本体内の最初の top-level `;` より後の宣言をメンバーとして誤検出しないこと。
        // メンバーには body range が無い（BodyStartLine == null）点をメソッド抽出と区別する。
        var content = "public enum Status {\n    ACTIVE,\n    INACTIVE;\n    public void activate() { ACTIVATE_HELPER(); }\n    private static void ACTIVATE_HELPER() {}\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ACTIVE" && s.ContainerName == "Status" && s.BodyStartLine == null);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "INACTIVE" && s.ContainerName == "Status" && s.BodyStartLine == null);
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "ACTIVATE_HELPER" && s.BodyStartLine == null);
    }

    [Fact]
    public void Extract_Java_DetectsDefaultAndSynchronizedMethods()
    {
        var content = "public interface Service {\n    default void init() { }\n    static Service create() { return null; }\n}\npublic class Worker {\n    synchronized void process() { }\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "Service");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "init");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "create");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "process");
    }

    [Fact]
    public void Extract_Kotlin_DetectsFunctionsAndClasses()
    {
        // Kotlin: class, fun / Kotlin: クラス、関数
        var content = """
            data class Config(val name: String)
            fun one() = 1
            fun process(input: String): String {
                return input.trim()
            }
            fun three() = 3
            """;
        var symbols = SymbolExtractor.Extract(1, "kotlin", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "name" && s.ContainerKind == "class" && s.ContainerName == "Config");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "one" && s.StartLine == 2 && s.EndLine == 2 && s.BodyStartLine == null && s.BodyEndLine == null);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "process" && s.StartLine == 3 && s.EndLine == 5 && s.BodyStartLine == 3 && s.BodyEndLine == 5);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "three" && s.StartLine == 6 && s.EndLine == 6 && s.BodyStartLine == null && s.BodyEndLine == null);
    }

    [Fact]
    public void Extract_Kotlin_DetectsTypealiasDeclarations()
    {
        var content = """
            typealias Handler = (String) -> Unit
            internal typealias UserMap = Map<String, User>
            public typealias Nested<T> = List<Pair<String, T>>

            val type = 1
            """;

        var symbols = SymbolExtractor.Extract(1, "kotlin", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Handler" && s.Visibility == null);
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "UserMap" && s.Visibility == "internal");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Nested" && s.Visibility == "public");
        Assert.DoesNotContain(symbols, s => s.Kind == "import" && s.Name == "type");
    }

    [Fact]
    public void Extract_Kotlin_DetectsSecondaryConstructors()
    {
        var content = """
            class Person(private val name: String) {
                var age: Int = 0

                constructor(name: String, age: Int) : this(name) { this.age = age }

                constructor() : this("anonymous", 0)

                fun greet(): String = "Hi $name"
            }

            class Box<T>(
                val value: T
            ) {
                private constructor(list: List<T>) : this(list.first())
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "kotlin", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Person");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Box");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "name" && s.ContainerKind == "class" && s.ContainerName == "Person" && s.Visibility == "private");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "value" && s.ContainerKind == "class" && s.ContainerName == "Box" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Person" && s.ContainerName == "Person" && s.Signature == "constructor(name: String, age: Int) : this(name) { this.age = age }");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Person" && s.ContainerName == "Person" && s.Signature == "constructor() : this(\"anonymous\", 0)");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Box" && s.ContainerName == "Box" && s.Visibility == "private" && s.Signature == "private constructor(list: List<T>) : this(list.first())");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "constructor");
    }

    [Fact]
    public void Extract_Kotlin_DetectsExpandedFeatures()
    {
        var content = "sealed interface Shape\nvalue class Email(val value: String)\ninner class Handler\n\ncompanion object {\n    const val MAX = 100\n}\n\nfun String.truncate(max: Int): String = take(max)\nsuspend fun fetchData(): List<Int> = emptyList()\ninline fun <reified T> parse(json: String): T = TODO()";
        var symbols = SymbolExtractor.Extract(1, "kotlin", content);

        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "Shape");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Email");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Handler");
        // Companion object (unnamed) / コンパニオンオブジェクト（無名）
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Companion" && s.Signature != null && s.Signature.Contains("companion object"));
        // Extension function / 拡張関数
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "truncate");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "fetchData");
        // const val / 定数プロパティ
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "MAX");
    }

    [Fact]
    public void Extract_Kotlin_DetectsInheritanceModifierFunctions()
    {
        var content = """
            package demo

            abstract class Base {
                abstract fun required(): Int
                open fun extensible(): String = "base"
                protected open fun scoped(): Int = 1
                internal abstract fun hidden(): Boolean
            }

            class Derived : Base() {
                override fun required(): Int = 42
                override fun extensible(): String = "child"
                final override fun scoped(): Int = 99
                override fun hidden(): Boolean = false
                public override fun toString(): String = "Derived"
                override suspend fun maybeAsync(): Int = 0

                final fun cannotOverride() {}
                private final fun noOverride() {}
                fun normal() {}
                suspend fun suspended() {}
                inline fun inlined() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "kotlin", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "required");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "extensible");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "scoped");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "hidden");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "toString");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "maybeAsync");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "cannotOverride");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "noOverride");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "normal");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "suspended");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "inlined");
    }

    [Fact]
    public void Extract_Kotlin_DetectsEnumEntries()
    {
        var content = """
            enum class Direction {
                NORTH,
                SOUTH,
                EAST,
                WEST
            }

            enum class HttpStatus(val code: Int) {
                OK(200),
                NOT_FOUND(404),
                SERVER_ERROR(500);

                fun isError(): Boolean = code >= 400
            }

            enum class Color(val rgb: Int) {
                RED(0xFF0000) {
                    override fun display() = "red"
                },
                GREEN(0x00FF00) {
                    override fun display() = "green"
                };

                abstract fun display(): String
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "kotlin", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "NORTH");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "SOUTH");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "EAST");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "WEST");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "OK");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "NOT_FOUND");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "SERVER_ERROR");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "RED");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "GREEN");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "rgb" && s.ContainerKind == "enum" && s.ContainerName == "Color");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "isError" && s.ContainerKind == "enum" && s.ContainerName == "HttpStatus");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "display" && s.Line == 18);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "display" && s.Line == 21);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "display" && s.Line == 24 && s.ReturnType == "String");
    }

    [Fact]
    public void Extract_Kotlin_AnonymousCompanionDefaultsToCompanionName()
    {
        var content = """
            class Widget {
                companion object {
                    const val MAX = 100
                    fun create(): Widget = Widget()
                }
            }

            class Named {
                companion object Factory {
                    fun build(): Named = Named()
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "kotlin", content);

        Assert.DoesNotContain(symbols, s => string.IsNullOrWhiteSpace(s.Name));

        var anonymousCompanion = Assert.Single(symbols.Where(s =>
            s.Kind == "class"
            && s.Name == "Companion"
            && s.ContainerKind == "class"
            && s.ContainerName == "Widget"));
        Assert.Equal("companion object {", anonymousCompanion.Signature);

        Assert.Contains(symbols, s =>
            s.Kind == "property"
            && s.Name == "MAX"
            && s.ContainerKind == "class"
            && s.ContainerName == "Companion");
        Assert.Contains(symbols, s =>
            s.Kind == "function"
            && s.Name == "create"
            && s.ContainerKind == "class"
            && s.ContainerName == "Companion");
        Assert.Contains(symbols, s =>
            s.Kind == "class"
            && s.Name == "Factory"
            && s.ContainerKind == "class"
            && s.ContainerName == "Named");
    }

    [Fact]
    public void Extract_Ruby_DetectsDefAndClass()
    {
        // Ruby: def, class, module / Ruby: メソッド、クラス、モジュール
        var content = "class UserService\n  def find_user(id)\n  end\nend";
        var symbols = SymbolExtractor.Extract(1, "ruby", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "find_user");
    }

    [Fact]
    public void Extract_PHP_DetectsFunctionsAndClasses()
    {
        // PHP: function, class, interface / PHP: 関数、クラス、インターフェース
        var content = "class AuthService {\n    public function login($user) {\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "php", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "AuthService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "login");
    }

    [Fact]
    public void Extract_Swift_DetectsFuncAndStruct()
    {
        // Swift: func, class, struct / Swift: 関数、クラス、構造体
        var content = "struct Config {\n    func validate() -> Bool {\n        return true\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "swift", content);

        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "validate");
    }

    [Fact]
    public void Extract_C_DetectsFunctionsAndStructs()
    {
        // C: functions, struct / C: 関数、構造体
        var content = "typedef struct Config {\n    int value;\n};\nint main(int argc) {\n}";
        var symbols = SymbolExtractor.Extract(1, "c", content);

        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "main");
    }

    [Fact]
    public void Extract_C_DoesNotCapturePrimitiveTypesForFunctionPointerTypedefs()
    {
        // C: function-pointer typedefs and ordinary functions / C: 関数ポインタ typedef と通常関数
        var content = """
            typedef int t_func_int_of_float_double(float, double);
            typedef int (*t_ptr_func_int_of_float_double)(float, double);
            typedef int (*t_ptr_func_int_of_float_complex)(float complex);
            typedef int (*t_ptr_func_int_of_double_complex)(double complex);
            static int add(int a, int b) {
                return a + b;
            }
            void my_callback(int x) {
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "c", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "add");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "my_callback");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && (s.Name == "int" || s.Name == "void"));
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "t_func_int_of_float_double");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "t_ptr_func_int_of_float_double");
    }

    [Fact]
    public void Extract_C_DetectsFunctionLikeAndObjectLikeMacros()
    {
        // C: function-like and object-like #define macros / C: 関数風・オブジェクト風 #define マクロ
        var content = """
            #include <stdio.h>

            #define MAX(a, b) ((a) > (b) ? (a) : (b))
            #define VERSION "1.0"
            #define MAX_BUFFER 4096

            void work(void) {
                int a = MAX(1, 2);
                printf("v=%s buf=%d\n", VERSION, MAX_BUFFER);
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "c", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MAX");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "VERSION");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MAX_BUFFER");
    }

    [Fact]
    public void Extract_Cpp_DetectsClassAndNamespace()
    {
        // C++: class, namespace, functions / C++: クラス、名前空間、関数
        var content = "namespace MyApp {\nclass Handler {\n    void process(int data) {\n    }\n};\n}";
        var symbols = SymbolExtractor.Extract(1, "cpp", content);

        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "MyApp");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Handler" && s.ContainerName == "MyApp");
    }

    [Fact]
    public void Extract_Cpp_DetectsFunctionLikeAndObjectLikeMacros()
    {
        // C++: function-like and object-like #define macros / C++: 関数風・オブジェクト風 #define マクロ
        var content = """
            #include <cstdio>

            #define MAX(a, b) ((a) > (b) ? (a) : (b))
            #define VERSION "1.0"
            #define MAX_BUFFER 4096

            void work() {
                auto a = MAX(1, 2);
                std::printf("v=%s buf=%d\n", VERSION, MAX_BUFFER);
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "cpp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MAX");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "VERSION");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MAX_BUFFER");
    }

    [Fact]
    public void Extract_Cpp_DoesNotCapturePrimitiveTypesForFunctionReturningPointerDeclarations()
    {
        // C++: function-returning-pointer declarations / C++: 関数が関数ポインタを返す宣言
        var content = """
            typedef int t_func_int_of_float_double(float, double);
            typedef int (*t_ptr_func_int_of_float_double)(float, double);
            extern int (*XSynchronize(
                Display* display,
                Bool onoff
            ))(
                Display* display
            );

            void my_callback(int x) {
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "cpp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "my_callback");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "int");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "void");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "t_func_int_of_float_double");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "t_ptr_func_int_of_float_double");
    }

    [Fact]
    public void Extract_TypeScript_DetectsInterfaceAndEnum()
    {
        // TypeScript: interface, type, enum / TypeScript: インターフェース、型、列挙型
        var content = "export interface IUser {\n    name: string;\n}\nexport enum Status {\n    Active,\n}";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "IUser");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Status");
    }

    [Fact]
    public void Extract_Haskell_DetectsTypeSignaturesAndDataTypes()
    {
        // Haskell: type signature, data, import / Haskell: 型シグネチャ、data型、import
        var content = "import Data.List\nimport qualified Data.Map as Map\n\ndata Tree a = Leaf | Node a (Tree a) (Tree a)\n\ninsert :: Ord a => a -> Tree a -> Tree a\ninsert x Leaf = Node x Leaf Leaf";
        var symbols = SymbolExtractor.Extract(1, "haskell", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Data.List");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Data.Map");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Tree");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "insert");
    }

    [Fact]
    public void Extract_Haskell_DoesNotTreatInstancesAsClasses()
    {
        // Haskell instance declarations should not be indexed as phantom type definitions.
        // Haskell の instance 宣言は phantom な型定義としてはインデックスしない。
        var content = """
            class Greeter a where
                greet :: a -> String

            data Person = Person

            instance Greeter Person where
                greet _ = "Hello"

            instance Greeter Int where
                greet _ = "Hi"
            """;

        var symbols = SymbolExtractor.Extract(1, "haskell", content);

        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "Greeter");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Person");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Greeter");
    }

    [Fact]
    public void Extract_FSharp_DetectsLetTypeModuleOpen()
    {
        // F#: let, type, module, open / F#: let束縛、型、モジュール、open
        var content = """
            module MyApp.Domain

            open System

            type UserId = int
            type User = { Name: string; Age: int }
            type Color = Red | Green | Blue
            type Person(name: string) =
                member _.Name = name

            let x = 1
            let mutable counter = 0
            let inline add x y = x + y
            let private secret = 42
            let internal hidden = "x"
            let ``spaced name`` = 1

            let validate user =
                user.Age > 0

            let rec factorial n =
                if n <= 1 then 1 else n * factorial (n - 1)
            """;
        var symbols = SymbolExtractor.Extract(1, "fsharp", content);

        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "MyApp.Domain");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "System");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "UserId");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Color");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Person");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "x");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "counter");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "add");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "secret");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "hidden");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "spaced name");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Name");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "validate");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "factorial");
    }

    [Fact]
    public void Extract_FSharp_DetectsNamespacesModulesAndMembers()
    {
        // F#: namespace rec, module private, member forms / F#: namespace rec、module private、member形
        var content = """
            namespace rec MyApp.Domain

            type Person(name: string) =
                member this.Name = name
                member _.Age = 0
                static member Create(name: string) = Person(name)
                override this.ToString() = this.Name

            type IVisitor =
                abstract member Visit : unit -> unit

            let validate user =
                user.Age > 0
            """;

        var symbols = SymbolExtractor.Extract(1, "fsharp", content);

        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "MyApp.Domain");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Person");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Name");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Age");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Create");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ToString");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Visit");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "validate");
    }

    [Fact]
    public void Extract_FSharp_DetectsValueBindings()
    {
        // F# value bindings should be indexed by their binding names / F# の値束縛は
        // 束縛名で索引される。
        var content = "let x = 5\nlet name = \"hello\"\nlet list = [1; 2; 3]";
        var symbols = SymbolExtractor.Extract(1, "fsharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "x");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "name");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "list");
    }

    [Fact]
    public void Extract_VB_DetectsCompoundVisibility()
    {
        // VB.NET compound visibility: Protected Friend / VB.NET 複合可視性
        var content = "Protected Friend Sub OnInit()\nEnd Sub\n\nPrivate Protected Function GetData() As String\n    Return \"\"\nEnd Function";
        var symbols = SymbolExtractor.Extract(1, "vb", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "OnInit");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetData");
    }

    [Fact]
    public void Extract_CSharp_DetectsPlainFieldDeclarations()
    {
        // Plain fields are now captured as kind `property` so definition/symbols/outline/
        // hotspots/unused can see the full member surface of a class. See issue #298.
        // 通常フィールドも kind `property` として抽出される（issue #298）。これにより
        // definition/symbols/outline/hotspots/unused がクラスの全メンバー形を見える。
        var content = "public class Config\n{\n    public string Name;\n    private int _count;\n    public readonly string Id = \"x\";\n    protected List<int> Items = new();\n    internal volatile bool IsReady;\n    public static int GlobalCount;\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Name" && s.ReturnType == "string" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "_count" && s.ReturnType == "int" && s.Visibility == "private");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Id" && s.ReturnType == "string" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Items" && s.Visibility == "protected");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "IsReady" && s.ReturnType == "bool" && s.Visibility == "internal");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "GlobalCount" && s.ReturnType == "int" && s.Visibility == "public");
        // const / static readonly keep kind `function` / const と static readonly は引き続き kind `function`
        Assert.DoesNotContain(symbols, s => s.Name == "Name" && s.Kind == "function");
        Assert.DoesNotContain(symbols, s => s.Name == "_count" && s.Kind == "function");
        Assert.DoesNotContain(symbols, s => s.Name == "Id" && s.Kind == "function");
    }

    [Fact]
    public void Extract_CSharp_PlainFieldPatternDoesNotLeakLocalVariables()
    {
        // Plain fields are captured as kind `property`, but local variable declarations
        // inside method / property accessor / constructor / lambda bodies share the same
        // shape as fields. Without a scope gate, names like `local`, `numbers`, `tmp`
        // would leak into `symbols`, `definition`, `outline`, `inspect`, and `unused`.
        // Closes #298 follow-up (codex review blocker).
        // 通常フィールドは kind `property` として抽出されるが、メソッド・アクセサ・
        // コンストラクタ・ラムダの内部にあるローカル変数宣言はフィールドと同じ形を持つ。
        // スコープ判定を入れないと `local`、`numbers`、`tmp` などが
        // `symbols` / `definition` / `outline` / `inspect` / `unused` に混入する。
        // Closes #298 の codex レビュー blocker 対応。
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public class Worker",
            "{",
            "    public string Field;",
            "    public List<int> Items = new();",
            "",
            "    public Worker()",
            "    {",
            "        string ctorLocal = \"ctor\";",
            "        List<int> ctorNumbers = new();",
            "    }",
            "",
            "    public void Run()",
            "    {",
            "        string local = \"x\";",
            "        System.Collections.Generic.List<int> numbers = new();",
            "        if (local.Length > 0)",
            "        {",
            "            string inner = local;",
            "        }",
            "    }",
            "",
            "    public int Value",
            "    {",
            "        get",
            "        {",
            "            int tmp = 1;",
            "            return tmp;",
            "        }",
            "    }",
            "",
            "    public Func<int, int> Lambda = x =>",
            "    {",
            "        int y = x + 1;",
            "        return y;",
            "    };",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Field");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Items");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Lambda");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Worker");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Run");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Value");

        Assert.DoesNotContain(symbols, s => s.Name == "ctorLocal");
        Assert.DoesNotContain(symbols, s => s.Name == "ctorNumbers");
        Assert.DoesNotContain(symbols, s => s.Name == "local");
        Assert.DoesNotContain(symbols, s => s.Name == "numbers");
        Assert.DoesNotContain(symbols, s => s.Name == "inner");
        Assert.DoesNotContain(symbols, s => s.Name == "tmp");
        Assert.DoesNotContain(symbols, s => s.Name == "y");
    }

    [Fact]
    public void Extract_CSharp_ConstLocalsAndQualifiedCallArguments_DoNotLeakPhantomFunctions()
    {
        var content = """
            using System;

            namespace Demo;

            public class Repro
            {
                public void M(TimeSpan elapsed)
                {
                    const string content = "hello";
                    Assert.True(
                        elapsed < TimeSpan.FromSeconds(10),
                        $"x {elapsed.TotalSeconds:F2}");
                }
            }

            public static class Assert
            {
                public static void True(bool condition, string message) { }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "Demo");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Repro");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "M");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Assert");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "True");

        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "content");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "FromSeconds");
    }

    [Fact]
    public void Extract_CSharp_VerbatimReturnTypeIdentifiers_AreNotRejectedBySuffixGuard()
    {
        var content = """
            namespace Demo;

            public class @new {}

            public class UsesVerbatim
            {
                public @new Make() => new @new();
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "new");

        var method = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Make"));
        Assert.Equal("@new", method.ReturnType);
    }

    [Fact]
    public void Extract_CSharp_ContextualKeywordReturnTypeIdentifiers_AreNotRejectedBySuffixGuard()
    {
        var content = """
            namespace Demo;

            public class await {}
            public class yield {}

            public class Uses
            {
                public await MakeAwait() => new await();
                public yield MakeYield() => new yield();
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "await");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "yield");

        var awaitMethod = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "MakeAwait"));
        Assert.Equal("await", awaitMethod.ReturnType);

        var yieldMethod = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "MakeYield"));
        Assert.Equal("yield", yieldMethod.ReturnType);
    }

    [Fact]
    public void Extract_CSharp_DetectsMultiLineFieldDeclaration()
    {
        // Plain field whose type occupies one line and whose name / initializer spill
        // onto the next line (`private Dictionary<string, int>\n    _map = new();`) must
        // still be captured as a single `property` symbol. The multi-line property match
        // builder combines the header and continuation lines before handing them to the
        // field regex. Closes #298 follow-up (codex adversarial review).
        // 型が 1 行目、名前と初期化式が次行へ回る通常フィールド
        // （`private Dictionary<string, int>\n    _map = new();`）も、1 件の `property`
        // シンボルとして抽出する。multi-line property match builder がヘッダ行と
        // 継続行を結合してから field regex に渡す。Closes #298 follow-up。
        var content = string.Join(
            "\n",
            "using System.Collections.Generic;",
            "namespace Demo;",
            "public class Store",
            "{",
            "    private Dictionary<string, int>",
            "        _map = new();",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property"
            && s.Name == "_map"
            && s.Visibility == "private"
            && s.ReturnType == "Dictionary<string,int>");
    }

    [Fact]
    public void Extract_CSharp_DetectsMultiLineFieldWithVolatileAndUnsafeModifiers()
    {
        // The multi-line header prefix check must accept field-only modifiers such as
        // `volatile`, `unsafe`, and `extern`, otherwise the combined match line is
        // never built and the declaration silently disappears from the index. Closes
        // #298 follow-up (second codex adversarial review).
        // multi-line ヘッダ判定は `volatile` / `unsafe` / `extern` のような field 固有の
        // 修飾子も受け入れないと、結合済みマッチ行が作られず宣言がインデックスから
        // 黙って消える。Closes #298 follow-up。
        var content = string.Join(
            "\n",
            "using System.Collections.Generic;",
            "namespace Demo;",
            "public unsafe class Edge",
            "{",
            "    private volatile Dictionary<string, int>",
            "        _map;",
            "    public unsafe delegate*<int, void>",
            "        Callback;",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property"
            && s.Name == "_map"
            && s.Visibility == "private"
            && s.ReturnType == "Dictionary<string,int>");
        Assert.Contains(symbols, s => s.Kind == "property"
            && s.Name == "Callback"
            && s.Visibility == "public");
    }

    [Fact]
    public void Extract_CSharp_DetectsMultiLineFieldWithParenthesizedInitializer()
    {
        // Multi-line fields whose initializer uses a constructor call or parenthesized
        // expression (`= new(\n    …);`) — including bodies that contain a lambda —
        // must still walk through the `(` and merge until the top-level `;`. Without
        // the depth-aware terminator, the earlier `(` break dropped the symbol.
        // Closes #298 follow-up (second codex adversarial review).
        // 複数行フィールドの初期化式がコンストラクタ呼び出しや括弧付き式
        // （`= new(\n    …);`、ラムダを含む場合も）であっても、`(` で打ち切らず
        // トップレベル `;` まで結合する。深さ追跡なしの `(` break ではシンボルが
        // 消えていた。Closes #298 follow-up。
        var content = string.Join(
            "\n",
            "using System;",
            "namespace Demo;",
            "public class Lazies",
            "{",
            "    private Lazy<int>",
            "        _value = new(",
            "            () => 42);",
            "    private Lazy<int>",
            "        _plain = new(",
            "            42);",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property"
            && s.Name == "_value"
            && s.Visibility == "private"
            && s.ReturnType == "Lazy<int>");
        Assert.Contains(symbols, s => s.Kind == "property"
            && s.Name == "_plain"
            && s.Visibility == "private"
            && s.ReturnType == "Lazy<int>");
    }

    [Fact]
    public void Extract_CSharp_DeclaratorListSurvivesComparisonInitializer()
    {
        // Declarator tail scanning must distinguish generic `<`/`>` from the comparison
        // operators inside initializers. Without a token-aware lookahead, expressions
        // like `_a = x < y ? 1 : 2, _b;` inflate the angle depth forever and drop the
        // trailing declarators. Closes #298 follow-up (second codex adversarial review).
        // declarator tail 走査は、初期化式内の比較演算子と generic の `<`/`>` を区別する
        // 必要がある。先読みなしでは `_a = x < y ? 1 : 2, _b;` のような初期化式で
        // angle 深さが 0 に戻らず、後続 declarator が消える。Closes #298 follow-up。
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "public class Compare",
            "{",
            "    private int x = 1, y = 2;",
            "    private int _a = x < y ? 1 : 2, _b;",
            "    private int _c = x > y ? 3 : 4, _d;",
            "    private int _e = new System.Collections.Generic.Dictionary<int, int>() { [x < y ? 1 : 2] = 0 }.Count, _f;",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "_a" && s.ReturnType == "int" && s.Visibility == "private");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "_b" && s.ReturnType == "int" && s.Visibility == "private");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "_c" && s.ReturnType == "int" && s.Visibility == "private");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "_d" && s.ReturnType == "int" && s.Visibility == "private");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "_e" && s.ReturnType == "int" && s.Visibility == "private");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "_f" && s.ReturnType == "int" && s.Visibility == "private");
    }

    [Fact]
    public void Extract_CSharp_DetectsMultiLineFieldWithObjectInitializer()
    {
        // Multi-line plain fields whose initializer uses an object or collection
        // initializer (`= new() { ... };`, `= new Dictionary<...> { ... };`) must
        // still complete at the real top-level `;`. The combined match line opens a
        // brace inside the initializer, so the field path cannot assume every `{` is
        // a property body — it must keep merging until the top-level semicolon closes
        // the declaration. Closes #298 follow-up (third codex adversarial review).
        // 複数行の通常フィールドで、`= new() { ... };` や `= new Dictionary<...> { ... };`
        // のようなオブジェクト/コレクション初期化子を使う宣言も、実際のトップレベル `;` で
        // 完了しなければならない。結合済みマッチ行には初期化子の `{` が入るため、field 経路は
        // あらゆる `{` を property 本体とみなしてはならず、宣言終端のトップレベル `;` まで
        // 結合を続ける必要がある。Closes #298 follow-up。
        var content = string.Join(
            "\n",
            "using System.Collections.Generic;",
            "namespace Demo;",
            "public class Containers",
            "{",
            "    private Dictionary<string, int>",
            "        _map = new()",
            "        {",
            "            [\"a\"] = 1",
            "        };",
            "    private List<int>",
            "        _list = new() {",
            "            1, 2, 3",
            "        };",
            "    private Dictionary<string, int>",
            "        _typed = new Dictionary<string, int>",
            "        {",
            "            [\"b\"] = 2",
            "        };",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property"
            && s.Name == "_map"
            && s.Visibility == "private"
            && s.ReturnType == "Dictionary<string,int>");
        Assert.Contains(symbols, s => s.Kind == "property"
            && s.Name == "_list"
            && s.Visibility == "private"
            && s.ReturnType == "List<int>");
        Assert.Contains(symbols, s => s.Kind == "property"
            && s.Name == "_typed"
            && s.Visibility == "private"
            && s.ReturnType == "Dictionary<string,int>");
    }

    [Fact]
    public void Extract_CSharp_DetectsWrappedRawStringFieldBeyondLookaheadBudget()
    {
        // issue #447 follow-up: once the declaration is confirmed at `Script = """`,
        // the extractor must continue linearly to the real `""";` terminator instead of
        // dropping the symbol at the 16-line confirmation cap.
        // issue #447 follow-up: `Script = """` で宣言確定後は、16 行の確認上限で
        // 打ち切らず、実際の `""";` 終端まで線形に継続してシンボルを保持する。
        var content = string.Join(
            "\n",
            [
                "namespace Demo;",
                "public class Fixtures",
                "{",
                "    private static readonly string",
                "        Script = \"\"\"",
                .. Enumerable.Range(1, 18).Select(i => $"line{i:00}"),
                "\"\"\";",
                "}"
            ]);
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var script = Assert.Single(symbols.Where(s => s.Kind == "function"
            && s.Name == "Script"
            && s.Visibility == "private"
            && s.ReturnType == "string"));
        Assert.Contains("Script = \"\"\"", script.Signature);
        Assert.Contains("\"\"\";", script.Signature);
    }

    [Fact]
    public void Extract_CSharp_DetectsSameLineConstRawStringFieldBeyondLookaheadBudget()
    {
        // Same-line `const string Name = """` must also enter the confirmed continuation
        // path immediately; otherwise the long raw-string body falls past the bounded
        // lookahead window and the stored signature truncates at the opener line.
        // 同一行の `const string Name = """` も確認済み継続へ即時に入らないと、
        // 長い raw string 本体が bounded な先読み窓の外へ落ち、保存 signature が
        // opener 行で途切れてしまう。
        var content = string.Join(
            "\n",
            [
                "namespace Demo;",
                "public class Fixtures",
                "{",
                "    private const string ConstScript = \"\"\"",
                .. Enumerable.Range(1, 18).Select(i => $"line{i:00}"),
                "\"\"\";",
                "}"
            ]);
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var constScript = Assert.Single(symbols.Where(s => s.Kind == "function"
            && s.Name == "ConstScript"
            && s.Visibility == "private"
            && s.ReturnType == "string"));
        Assert.Contains("ConstScript = \"\"\"", constScript.Signature);
        Assert.Contains("\"\"\";", constScript.Signature);
    }

    [Fact]
    public void Extract_CSharp_DetectsLongObjectInitializerBeyondLookaheadBudget()
    {
        // issue #447 follow-up: long object/collection initializers must keep consuming
        // lines after the declaration is confirmed at `_map = new()`, rather than falling
        // back to the raw header once the bounded confirmation phase expires.
        // issue #447 follow-up: `_map = new()` で宣言確定後は、長い object/collection
        // initializer でも bounded な確認フェーズ満了で raw header に戻らず、そのまま
        // 継続して終端 `;` まで追跡しなければならない。
        var initializerLines = Enumerable.Range(1, 18)
            .Select(i => $"            [\"k{i:00}\"] = {i}")
            .ToArray();
        var content = string.Join(
            "\n",
            [
                "using System.Collections.Generic;",
                "namespace Demo;",
                "public class Containers",
                "{",
                "    private Dictionary<string, int>",
                "        _map = new()",
                "        {",
                .. initializerLines,
                "        };",
                "}"
            ]);
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var map = Assert.Single(symbols.Where(s => s.Kind == "property"
            && s.Name == "_map"
            && s.Visibility == "private"
            && s.ReturnType == "Dictionary<string,int>"));
        Assert.Contains("_map = new()", map.Signature);
        Assert.Contains("};", map.Signature);
    }

    [Fact]
    public void Extract_CSharp_MultiLineFieldIgnoresBraceInsideStringLiteral()
    {
        // Brace detection must use the sanitized match line, not the raw source, so a
        // `{` that lives inside a string literal or comment doesn't flip the field path
        // into property-body handling and then silently drop the declaration. Closes
        // #298 follow-up (third codex adversarial review).
        // brace 検出はサニタイズ済みのマッチ行で行わなければならない。raw 行を見ると
        // 文字列リテラルやコメント内の `{` で field 経路が property 本体扱いに切り替わり、
        // 宣言が黙って消える恐れがあるため。Closes #298 follow-up。
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "public class Templates",
            "{",
            "    private string",
            "        _open = \"{\";",
            "    private string",
            "        _pair = \"{\" + \"}\";",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property"
            && s.Name == "_open"
            && s.Visibility == "private"
            && s.ReturnType == "string");
        Assert.Contains(symbols, s => s.Kind == "property"
            && s.Name == "_pair"
            && s.Visibility == "private"
            && s.ReturnType == "string");
    }

    [Fact]
    public void Extract_CSharp_DetectsDeclaratorListFields()
    {
        // `private int _x, _y;` must emit one `property` symbol per declarator. The
        // field regex greedily swallows earlier declarators into `returnType`, so the
        // post-match expander walks the top-level commas in `returnType` and the tail
        // after the match to recover every declarator name. Closes #298 follow-up
        // (codex adversarial review).
        // `private int _x, _y;` のような declarator list は declarator ごとに 1 件の
        // `property` シンボルを発行する。field regex は前段の declarator を
        // returnType に飲み込むため、post-match 展開で returnType のトップレベル `,`
        // とマッチ後テールを走査し、すべての declarator 名を復元する。
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "public class Holder",
            "{",
            "    private int _x, _y;",
            "    public string First, Second, Third;",
            "    private int _a = 1, _b, _c = 3;",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "_x" && s.ReturnType == "int" && s.Visibility == "private");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "_y" && s.ReturnType == "int" && s.Visibility == "private");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "First" && s.ReturnType == "string" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Second" && s.ReturnType == "string" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Third" && s.ReturnType == "string" && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "_a" && s.ReturnType == "int" && s.Visibility == "private");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "_b" && s.ReturnType == "int" && s.Visibility == "private");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "_c" && s.ReturnType == "int" && s.Visibility == "private");
        // The bogus `int _x,` or `int _a = 1,` returnType from a single-symbol emit must
        // not leak into the index. 単一シンボル発行で紛れ込む `int _x,` 等の returnType は
        // インデックスに漏らさない。
        Assert.DoesNotContain(symbols, s => s.ReturnType != null && s.ReturnType.Contains(','));
    }

    [Fact]
    public void Extract_CSharp_SameLineDeclaratorListsStillExpandAfterEarlierSiblings()
    {
        // Same-line field declarator lists must still expand trailing declarators even
        // when the field statement is only reached after an earlier sibling on the same
        // physical line restarts matching. The current branch already handles the #582
        // repro shapes; this test locks that behavior so a future column-domain mismatch
        // does not silently drop `B`. Closes #582.
        // 同一物理行で先行 sibling の後ろから field 文に再入した場合でも、same-line の
        // declarator list は末尾 declarator を展開し続けなければならない。現行ブランチは
        // #582 の再現形を既に正しく処理できるため、このテストで将来の列座標ずれ回帰から
        // `B` が無言で欠落するのを防ぐ。Closes #582.
        var cases = new[]
        {
            new
            {
                Content = "public class C { public void M() { } public int A = 1, B; }",
                SiblingKind = "function",
                SiblingName = "M",
                Signature = "public int A = 1, B;"
            },
            new
            {
                Content = "public class C { public int P { get; set; } public int A, B; }",
                SiblingKind = "property",
                SiblingName = "P",
                Signature = "public int A, B;"
            },
            new
            {
                Content = "public class C { public class N { } public int A = 1, B; }",
                SiblingKind = "class",
                SiblingName = "N",
                Signature = "public int A = 1, B;"
            }
        };

        foreach (var @case in cases)
        {
            var symbols = SymbolExtractor.Extract(1, "csharp", @case.Content);

            Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "C");
            Assert.Contains(symbols, s => s.Kind == @case.SiblingKind && s.Name == @case.SiblingName
                && s.ContainerKind == "class" && s.ContainerName == "C");

            var a = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "A"));
            Assert.Equal("class", a.ContainerKind);
            Assert.Equal("C", a.ContainerName);
            Assert.Equal(@case.Signature, a.Signature);

            var b = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "B"));
            Assert.Equal("class", b.ContainerKind);
            Assert.Equal("C", b.ContainerName);
            Assert.Equal(@case.Signature, b.Signature);
        }
    }

    [Fact]
    public void Extract_CSharp_EmptySameLineNestedTypeStillExposesLaterSiblingType()
    {
        // Stepping into a same-line nested type body must only happen when there is an
        // actual member after the opening `{`. For an empty nested interface body, the
        // next statement start is the closing `}`, and restarting there would skip the
        // later same-line sibling type entirely. Closes #585.
        // same-line の nested type 本体へ潜る再開は、開き `{` の後に実際の member がある
        // ときだけ行う必要がある。空の nested interface 本体では次の文頭が closing `}`
        // になり、そこへ再開すると後続の same-line sibling type が丸ごと落ちる。
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "[A]",
            "public class Outer { public interface I<T1,           T2> { } public class Sibling { } }");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "interface"
            && s.Name == "I"
            && s.ContainerName == "Outer");
        Assert.Contains(symbols, s => s.Kind == "class"
            && s.Name == "Sibling"
            && s.ContainerName == "Outer"
            && s.Signature == "public class Sibling { }");
    }

    [Fact]
    public void Extract_CSharp_EmptySameLineNestedTypeStillExposesLaterOuterProperty()
    {
        // When a real nested same-line type ends before a later outer sibling property on
        // the same physical line, extraction must skip the nested type's closing `}` and
        // resume at the later property instead of treating the empty body as a restart
        // target. This is the closing-line outer-sibling variant found during review.
        // 実在する same-line nested type の後ろに outer 側の sibling property が同じ物理行
        // で続く場合、抽出は nested type の closing `}` を飛ばして後続 property から
        // 再開しなければならない。空本体そのものを再開先にしてしまうと outer sibling が
        // 欠落する。review で見つかった closing-line outer-sibling 変種を固定する。
        var content = """
            namespace Demo;

            public class Host
            {
                public class Wrapped<T>
                    where T : class
                {
                    public class Child { } } public int P { get; set; }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class"
            && s.Name == "Child"
            && s.ContainerName == "Wrapped");
        Assert.Contains(symbols, s => s.Kind == "property"
            && s.Name == "P"
            && s.ContainerName == "Host"
            && s.Signature == "public int P { get; set; }");
    }

    [Fact]
    public void Extract_CSharp_SameLineClassBodyFieldIsCapturedAndLocalIsRejected()
    {
        // Column-aware scope tracking: `public class C { public int X; }` must capture
        // both the outer class C and the inner field X. Before #400, the type-body gate
        // only looked at the scope at line start, so a same-line class body looked like
        // "not inside a type body" and X was silently dropped. Conversely,
        // `public void M() { int local = 1; }` inside a class must NOT emit a phantom
        // `property local`: column-wise, col where `int local` starts sits inside a
        // method body (not a class body), and the column-aware gate correctly rejects it.
        // Closes #400.
        // 列単位のスコープ追跡: `public class C { public int X; }` では外側の class C と
        // 同一行のフィールド X をどちらも拾う必要がある。#400 以前は line-start のみを
        // 見ていたため、同一行の class body は「型本体の中ではない」と誤判定され X が
        // 取りこぼされた。逆に class 内の `public void M() { int local = 1; }` では、
        // `int local` の列が method body の中（class body ではない）であることを列意識
        // ゲートが認識するため、擬似的な `property local` を生成してはならない。
        // Closes #400.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public class C { public int X; }",
            "",
            "public class D",
            "{",
            "    public void M() { int local = 1; }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "C");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "X"
            && s.ContainerKind == "class" && s.ContainerName == "C"
            && s.Signature == "public int X;");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "D");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "M"
            && s.ContainerKind == "class" && s.ContainerName == "D");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "local");
    }

    [Fact]
    public void Extract_CSharp_SameLineAutoPropertiesInsideTypeBodiesAreCaptured()
    {
        // Same-line C# type bodies already recover methods, events, and plain fields, so
        // brace-body auto-properties must also survive when nested inside the same
        // `class/struct/interface { ... }` physical line. Before #470, the brace-property
        // skip guard inspected the line's first `{`, which belongs to the enclosing type
        // body, and silently discarded `P { get; set; }` / `R { get; }` as "not a
        // property". Closes #470.
        // 同一行 C# 型本体では method / event / plain field は既に復元できるため、
        // `class/struct/interface { ... }` と同じ物理行にある brace-body auto-property も
        // 抽出されなければならない。#470 前は brace-property の skip guard が行頭側の
        // 最初の `{`（外側型本体）を見てしまい、`P { get; set; }` / `R { get; }` を
        // 「property ではない」と誤判定して無言で捨てていた。Closes #470.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public class C { public int P { get; set; } }",
            "public struct S { public int Q { get; set; } }",
            "public interface I { int R { get; } }",
            "public class MethodsOk { public void M() { } }",
            "public class EventsOk { public event System.EventHandler E; }");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var p = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("class", p.ContainerKind);
        Assert.Equal("C", p.ContainerName);
        Assert.Equal("public int P { get; set; }", p.Signature);

        var q = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Q"));
        Assert.Equal("struct", q.ContainerKind);
        Assert.Equal("S", q.ContainerName);
        Assert.Equal("public int Q { get; set; }", q.Signature);

        var r = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "R"));
        Assert.Equal("interface", r.ContainerKind);
        Assert.Equal("I", r.ContainerName);
        Assert.Equal("int R { get; }", r.Signature);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "M"
            && s.ContainerKind == "class" && s.ContainerName == "MethodsOk");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "E"
            && s.ContainerKind == "class" && s.ContainerName == "EventsOk");
    }

    [Fact]
    public void Extract_CSharp_SameLineAutoPropertyAfterExpressionBodiedMethodIsCaptured()
    {
        // Outer-type false positives must still be skipped even when a later same-line
        // member introduces `=>`. Before the follow-up fix for #470, the brace-property
        // guard looked for `=>` anywhere in the remaining line, so
        // `public class C { public int M() => 1; public int P { get; set; } }`
        // treated the outer class header as an "expression-bodied property" and broke
        // before reaching `P`. Closes #470.
        // 同一行後半の member が `=>` を含んでいても、outer type 由来の偽陽性は
        // 引き続き弾かれなければならない。#470 の追修正前は brace-property guard が
        // 行末までのどこかに `=>` があるだけで式本体 property 扱いしてしまい、
        // `public class C { public int M() => 1; public int P { get; set; } }`
        // で outer class header を誤許可し、`P` まで到達できなかった。Closes #470.
        var content = "public class C { public int M() => 1; public int P { get; set; } }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "C"));

        var method = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "M"));
        Assert.Equal("class", method.ContainerKind);
        Assert.Equal("C", method.ContainerName);
        Assert.Equal("public int M() => 1;", method.Signature);

        var property = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("class", property.ContainerKind);
        Assert.Equal("C", property.ContainerName);
        Assert.Equal("public int P { get; set; }", property.Signature);
    }

    [Fact]
    public void Extract_CSharp_HeaderLineAutoPropertyInsideMultilineTypeBodyIsCaptured()
    {
        // A C# type can open its body on the header line while still closing on a later
        // line (`public class C { public int P { get; }` + next-line `}`). Before #580,
        // the outer class/struct/interface match stopped the same-line scan because the
        // type body was not fully compact on one line, so the first member that shared
        // the header line silently disappeared. Closes #580.
        // C# の型は、本体開始 `{` をヘッダ行に置いたまま閉じ `}` を後続行へ送れる
        // (`public class C { public int P { get; }` + 次行 `}`)。#580 前は outer
        // class/struct/interface のマッチ時点で same-line scan が止まり、ヘッダ行を
        // 共有する最初の member が無言で脱落していた。Closes #580.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public class Outer { public int P { get; }",
            "}",
            "public struct Holder { public int Q { get; }",
            "}",
            "public interface IOuter { int R { get; }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var p = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("class", p.ContainerKind);
        Assert.Equal("Outer", p.ContainerName);
        Assert.Equal("public int P { get; }", p.Signature);

        var q = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Q"));
        Assert.Equal("struct", q.ContainerKind);
        Assert.Equal("Holder", q.ContainerName);
        Assert.Equal("public int Q { get; }", q.Signature);

        var r = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "R"));
        Assert.Equal("interface", r.ContainerKind);
        Assert.Equal("IOuter", r.ContainerName);
        Assert.Equal("int R { get; }", r.Signature);

        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "Outer");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "Holder");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "IOuter");
    }

    [Fact]
    public void Extract_CSharp_SameLineAutoPropertyAfterExpressionBodiedPropertyIsCaptured()
    {
        // Same-line C# type bodies must not skip the first real member just because an
        // outer-type false-positive property candidate overran into a later sibling while
        // scanning for `{` / `=>`. In
        // `public class C { public int A => 1; public int P { get; set; } }`,
        // both `A` and `P` must survive and there must be no phantom `property C`.
        // Closes #472.
        // 同一行 C# 型本体では、outer-type 由来の偽 property 候補が後続 sibling まで
        // 食い込んだとしても、最初の本物 member を飛ばしてはならない。
        // `public class C { public int A => 1; public int P { get; set; } }`
        // では `A` と `P` の両方が抽出され、phantom `property C` が出てはいけない。
        // Closes #472.
        var content = "public class C { public int A => 1; public int P { get; set; } }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "C"));

        var expressionProperty = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "A"));
        Assert.Equal("class", expressionProperty.ContainerKind);
        Assert.Equal("C", expressionProperty.ContainerName);
        Assert.Equal("public int A => 1;", expressionProperty.Signature);

        var autoProperty = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("class", autoProperty.ContainerKind);
        Assert.Equal("C", autoProperty.ContainerName);
        Assert.Equal("public int P { get; set; }", autoProperty.Signature);

        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "C");
    }

    [Fact]
    public void Extract_CSharp_MultiLineConstantPatterns_DoNotBecomePhantomProperties()
    {
        // issue #779: multi-line expression-bodied constant patterns (`value is` + later-line
        // `Red` / `or Red`) must stay inside the enclosing method body. Before the fix, the
        // continuation lines re-entered the plain-field regex, emitted phantom `property Red`
        // rows, and downstream reference extraction suppressed the real pattern heads.
        // issue #779: 複数行の式本体 constant pattern（`value is` の次行に `Red` / `or Red`）
        // は enclosing method body の内部として扱われなければならない。修正前は継続行が
        // plain-field regex に再突入して phantom `property Red` を出し、後段の reference
        // 抽出が本物の pattern head を抑止していた。
        const string content = """
            using static Demo.Color;

            namespace Demo;

            public enum Color
            {
                Red
            }

            public sealed class Uses
            {
                public bool Match(object value) => value is
                    Red
                    or
                    Red;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var match = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Match"));
        Assert.Equal(12, match.StartLine);
        Assert.Equal(15, match.EndLine);
        Assert.Equal(12, match.BodyStartLine);
        Assert.Equal(15, match.BodyEndLine);

        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "Red" && s.ContainerName == "Uses");
    }

    [Fact]
    public void Extract_CSharp_MultiLineExpressionBodiedMethod_TerminatorLineKeepsFullSignatureAndSiblingField()
    {
        // issue #835 / #836: when a multi-line expression-bodied member ends on the next line,
        // a real same-line sibling after that terminating `;` must still be extracted, and the
        // stored signature must include the continuation line through the terminator.
        // issue #835 / #836: 複数行の式本体メンバーが次行の `;` で終わる場合でも、その
        // `;` の後ろにある同一行 sibling は抽出されなければならず、保存される signature も
        // 継続行を含めて終端 `;` までを保持していなければならない。
        const string content = """
            namespace Demo;

            public enum Color
            {
                Red
            }

            public sealed class Uses
            {
                public bool Match(object value) => value is
                    Red; public int X;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var match = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Match"));
        Assert.Equal(10, match.StartLine);
        Assert.Equal(11, match.EndLine);
        Assert.Equal(10, match.BodyStartLine);
        Assert.Equal(11, match.BodyEndLine);
        Assert.Equal("public bool Match(object value) => value is Red;", match.Signature);

        var x = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "X"));
        Assert.Equal(11, x.StartLine);
        Assert.Equal(11, x.EndLine);
        Assert.Equal("Uses", x.ContainerName);
        Assert.Equal("class", x.ContainerKind);
        Assert.Equal("public int X;", x.Signature);
    }

    [Fact]
    public void Extract_CSharp_SameLineAutoPropertyBeforeMethodIsCaptured()
    {
        // Mixed-kind same-line siblings must not lose the earlier brace-body property when a
        // later method on the same physical line needs a different regex family. This locks the
        // property side of `property -> method` so the outer-type false-positive skip and the
        // same-line continuation machinery do not silently leave only the method behind.
        // Closes #472 / #473 follow-up.
        // 同一行の mixed-kind sibling では、後続 method が別 regex 群を必要とするからと
        // いって、手前の brace-body property を落としてはならない。outer type 偽陽性の
        // skip と same-line 継続処理が組み合わさって method だけ残る退行を防ぐため、
        // `property -> method` の property 側を固定する。Closes #472 / #473 follow-up.
        var content = "public class C { public int P { get; set; } public void M() { } }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "C"));

        var property = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("class", property.ContainerKind);
        Assert.Equal("C", property.ContainerName);
        Assert.Equal("public int P { get; set; }", property.Signature);

        var method = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "M"));
        Assert.Equal("class", method.ContainerKind);
        Assert.Equal("C", method.ContainerName);
        Assert.Equal("public void M() { }", method.Signature);
    }

    [Fact]
    public void Extract_CSharp_SameLineAutoPropertyBeforeEventIsCaptured()
    {
        // The same continuation path must also preserve brace-body properties when the next
        // same-line sibling is an event declaration instead of a method. This guards the
        // `property -> event` shape covered by the mixed-kind same-line follow-up issue.
        // Closes #472 / #473 follow-up.
        // 同じ継続経路は、次の same-line sibling が method ではなく event の場合でも
        // brace-body property を保持しなければならない。mixed-kind same-line の追件で
        // 問題になった `property -> event` 形をここで固定する。Closes #472 / #473 follow-up.
        var content = "public class C { public int P { get; set; } public event System.EventHandler E; }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "C"));

        var property = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("class", property.ContainerKind);
        Assert.Equal("C", property.ContainerName);
        Assert.Equal("public int P { get; set; }", property.Signature);

        var eventSymbol = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E"));
        Assert.Equal("class", eventSymbol.ContainerKind);
        Assert.Equal("C", eventSymbol.ContainerName);
        Assert.Equal("public event System.EventHandler E;", eventSymbol.Signature);
    }

    [Fact]
    public void Extract_CSharp_SameLineEventBeforeMethodIsCaptured()
    {
        // Mixed-kind same-line siblings must also preserve an earlier event when a later
        // method shares the same physical line. Without a C#-specific defer/restart path,
        // the method regex can claim the later sibling before the event row ever runs and
        // silently drop `event E`.
        // Closes #473 follow-up.
        // 同一行の mixed-kind sibling では、後続 method が同じ物理行にある場合でも
        // 手前の event を落としてはならない。C# 専用の defer/restart 経路が無いと、
        // method regex が先に後続 sibling を取って `event E` が無言で欠落する。
        // Closes #473 follow-up.
        var content = "public class C { public event System.EventHandler E; public void M() { } }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "C"));

        var eventSymbol = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E"));
        Assert.Equal("class", eventSymbol.ContainerKind);
        Assert.Equal("C", eventSymbol.ContainerName);
        Assert.Equal("public event System.EventHandler E;", eventSymbol.Signature);

        var method = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "M"));
        Assert.Equal("class", method.ContainerKind);
        Assert.Equal("C", method.ContainerName);
        Assert.Equal("public void M() { }", method.Signature);
    }

    [Fact]
    public void Extract_CSharp_SameLineAccessorEventBeforeMethodIsCaptured()
    {
        // Accessor-bodied events use `{ add/remove }` instead of `;`, so same-line mixed-kind
        // recovery must treat the accessor body as the event's boundary and still restart at
        // the following sibling method. Otherwise the event signature absorbs `public void M`
        // and the method disappears.
        // Closes #473 follow-up.
        // アクセサ本体付き event は `;` ではなく `{ add/remove }` を持つため、same-line の
        // mixed-kind 回復でも event 本体終端を境界として扱い、後続 method 位置から再開
        // しなければならない。そうしないと event の signature が `public void M` を
        // 飲み込み、method 自体が消える。Closes #473 follow-up.
        var content = "public class C { public event System.Action E { add { } remove { } } public void M() { } }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "C"));

        var eventSymbol = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E"));
        Assert.Equal("class", eventSymbol.ContainerKind);
        Assert.Equal("C", eventSymbol.ContainerName);
        Assert.Equal("public event System.Action E { add { } remove { } }", eventSymbol.Signature);

        var method = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "M"));
        Assert.Equal("class", method.ContainerKind);
        Assert.Equal("C", method.ContainerName);
        Assert.Equal("public void M() { }", method.Signature);
    }

    [Fact]
    public void Extract_CSharp_SameLineAccessorEventBeforePropertyIsCaptured()
    {
        // Accessor-bodied events must stop at their own closing `}` even when the next same-line
        // sibling is a property. Otherwise the semicolon fallback keeps scanning until the later
        // property terminator, the event signature absorbs `public int P`, and the property never
        // gets a restart chance. Closes #519.
        // accessor body を持つ event は、次の same-line sibling が property の場合でも
        // 自身の閉じ `}` で終端しなければならない。そうしないと semicolon fallback が
        // 後続 property の終端まで進み、event signature に `public int P` が混入し、
        // property 側の再開機会も失われる。Closes #519.
        var content = "public class C { public event System.Action E { add { } remove { } } public int P { get; set; } }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "C"));

        var eventSymbol = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E"));
        Assert.Equal("class", eventSymbol.ContainerKind);
        Assert.Equal("C", eventSymbol.ContainerName);
        Assert.Equal("public event System.Action E { add { } remove { } }", eventSymbol.Signature);

        var property = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("class", property.ContainerKind);
        Assert.Equal("C", property.ContainerName);
        Assert.Equal("public int P { get; set; }", property.Signature);
    }

    [Fact]
    public void Extract_CSharp_SameLineEventBeforeDelegateIsCaptured()
    {
        // Delegate rows sit ahead of event rows in the C# pattern list, so a failed delegate
        // match at `event E;` must not keep scanning forward and claim the later delegate.
        // Otherwise the earlier event never reaches its own regex family and silently drops.
        // Closes #522.
        // C# の pattern 順では delegate 行が event 行より前にあるため、`event E;` で失敗した
        // delegate row が後続 delegate まで進んではならない。進んでしまうと手前の event が
        // 自分の regex family に到達できず、無言で欠落する。Closes #522.
        var content = "public class C { public event System.Action E; public delegate void D(); }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "C"));

        var eventSymbol = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E"));
        Assert.Equal("class", eventSymbol.ContainerKind);
        Assert.Equal("C", eventSymbol.ContainerName);
        Assert.Equal("public event System.Action E;", eventSymbol.Signature);

        var delegateSymbol = Assert.Single(symbols.Where(s => s.Kind == "delegate" && s.Name == "D"));
        Assert.Equal("class", delegateSymbol.ContainerKind);
        Assert.Equal("C", delegateSymbol.ContainerName);
        Assert.Equal("public delegate void D();", delegateSymbol.Signature);
    }

    [Fact]
    public void Extract_CSharp_SameLineAccessorEventBeforeDelegateIsCaptured()
    {
        // The same cross-family starvation also applies when the earlier declaration is an
        // accessor-bodied event: the event must clamp at its own accessor block, and the later
        // delegate must only be reached via the explicit restart path rather than by skipping
        // past the event statement. Closes #519 / #522.
        // 同じ cross-family の starvation は、手前が accessor-bodied event の場合にも起こる。
        // event 自身は accessor block で正しく切れ、後続 delegate には event 文を飛び越える
        // のではなく明示的な restart 経路で到達しなければならない。Closes #519 / #522.
        var content = "public class C { public event System.Action E { add { } remove { } } public delegate void D(); }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "C"));

        var eventSymbol = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E"));
        Assert.Equal("class", eventSymbol.ContainerKind);
        Assert.Equal("C", eventSymbol.ContainerName);
        Assert.Equal("public event System.Action E { add { } remove { } }", eventSymbol.Signature);

        var delegateSymbol = Assert.Single(symbols.Where(s => s.Kind == "delegate" && s.Name == "D"));
        Assert.Equal("class", delegateSymbol.ContainerKind);
        Assert.Equal("C", delegateSymbol.ContainerName);
        Assert.Equal("public delegate void D();", delegateSymbol.Signature);
    }

    [Fact]
    public void Extract_CSharp_SameLineDelegateBeforeAccessorEventWithLaterSiblingIsCaptured()
    {
        // After a leading delegate restarts the same-line scan at a later accessor-bodied event,
        // the defer checks for function/property/event/delegate rows must still see the raw event
        // statement start. If they inspect the property/function merged candidate instead, they
        // can skip directly to the trailing sibling and silently drop the middle custom event.
        // Lock the issue #603 repro plus the sibling-family matrix (`property` / `method` /
        // `delegate` / `event`) in one fixture. Closes #603.
        // 先頭 delegate から later accessor-bodied event へ same-line restart した後でも、
        // function/property/event/delegate 各 row の defer 判定は raw の event 文開始位置を
        // 見続けなければならない。property/function 用の merged candidate を見てしまうと、
        // 後続 sibling へ直接飛んで中間 custom event が無言で欠落する。#603 の最小再現と、
        // 後続 sibling family (`property` / `method` / `delegate` / `event`) を 1 fixture で
        // 固定する。Closes #603.
        var content = string.Join(
            "\n",
            "public class PropertyCase { public delegate void D(); public event System.Action E { add { } remove { } } public int P { get; set; } }",
            "public class MethodCase { public delegate void D(); public event System.Action E { add { } remove { } } public void M() { } }",
            "public class DelegateCase { public delegate void D1(); public event System.Action E { add { } remove { } } public delegate void D2(); }",
            "public class EventCase { public delegate void D1(); public event System.Action E { add { } remove { } } public event System.Action E2; }");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var propertyEvent = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E" && s.ContainerName == "PropertyCase"));
        Assert.Equal("public event System.Action E { add { } remove { } }", propertyEvent.Signature);
        var property = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P" && s.ContainerName == "PropertyCase"));
        Assert.Equal("public int P { get; set; }", property.Signature);

        var methodEvent = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E" && s.ContainerName == "MethodCase"));
        Assert.Equal("public event System.Action E { add { } remove { } }", methodEvent.Signature);
        var method = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "M" && s.ContainerName == "MethodCase"));
        Assert.Equal("public void M() { }", method.Signature);

        var delegateEvent = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E" && s.ContainerName == "DelegateCase"));
        Assert.Equal("public event System.Action E { add { } remove { } }", delegateEvent.Signature);
        var delegateTail = Assert.Single(symbols.Where(s => s.Kind == "delegate" && s.Name == "D2" && s.ContainerName == "DelegateCase"));
        Assert.Equal("public delegate void D2();", delegateTail.Signature);

        var eventEvent = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E" && s.ContainerName == "EventCase"));
        Assert.Equal("public event System.Action E { add { } remove { } }", eventEvent.Signature);
        var trailingEvent = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E2" && s.ContainerName == "EventCase"));
        Assert.Equal("public event System.Action E2;", trailingEvent.Signature);
    }

    [Fact]
    public void Extract_CSharp_SameLineAutoPropertyAfterConstructorsIsCaptured()
    {
        // Same-line C# constructors must not stop later sibling declarations from
        // reaching their own patterns. The #470 follow-up initially only resumed
        // after method-like patterns with a return type, so
        // `public class C { public C() { } public int P { get; set; } }`
        // still dropped `P` while the same shape after a normal method worked.
        // Issue #478 showed an additional starvation path: the dedicated static-ctor
        // regex sat after property rows, so
        // `public class D { static D() { } public int Q { get; set; } }`
        // indexed `Q` but silently lost the static ctor itself. Lock both ctor kinds
        // and the later properties in one same-line fixture. Closes #470 / #478.
        // 同一行の C# constructor は、その後ろに続く sibling 宣言の pattern 到達を
        // 止めてはならない。#470 の追修正当初は戻り値型を持つ method 系だけを再開
        // していたため、`public class C { public C() { } public int P { get; set; } }`
        // では通常 method 後と違って `P` がまだ落ちていた。さらに #478 では、
        // static ctor 専用 regex が property 行より後ろにあったため
        // `public class D { static D() { } public int Q { get; set; } }`
        // で `Q` は出ても static ctor 自体が欠落していた。instance / static ctor と
        // 後続 property の両方をこの same-line fixture で固定する。Closes #470 / #478.
        var content = string.Join(
            "\n",
            "public class C { public C() { } public int P { get; set; } }",
            "public class D { static D() { } public int Q { get; set; } }");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var ctor = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "C"));
        Assert.Equal("class", ctor.ContainerKind);
        Assert.Equal("C", ctor.ContainerName);

        var p = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("class", p.ContainerKind);
        Assert.Equal("C", p.ContainerName);
        Assert.Equal("public int P { get; set; }", p.Signature);

        var staticCtor = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "D"));
        Assert.Equal("class", staticCtor.ContainerKind);
        Assert.Equal("D", staticCtor.ContainerName);
        Assert.Equal("static D() { }", staticCtor.Signature);

        var q = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Q"));
        Assert.Equal("class", q.ContainerKind);
        Assert.Equal("D", q.ContainerName);
        Assert.Equal("public int Q { get; set; }", q.Signature);
    }

    [Fact]
    public void Extract_CSharp_SameLineMixedMemberKindsAreAllCaptured()
    {
        // Compact same-line C# type bodies must behave like a sibling stream even when
        // adjacent declarations are different member kinds. Before #473, `event E;`
        // short-circuited later auto-properties, and interface-style `void M();`
        // swallowed the following property into the method signature. Guard both
        // issue repros plus the reverse `property + event` order in one fixture.
        // 同一行のコンパクトな C# 型本体は、隣接宣言が異なる member kind でも
        // sibling ストリームとして扱われなければならない。#473 前は `event E;` が
        // 後続 auto-property を止め、interface 形の `void M();` は後続 property を
        // method signature に飲み込んでいた。issue の最小再現に加え、逆順の
        // `property + event` も 1 つの fixture で固定する。Closes #473.
        var content = string.Join(
            "\n",
            "public class C { public event System.EventHandler E; public int P { get; set; } }",
            "public struct S { public int Q { get; set; } public event System.EventHandler F; }",
            "public interface I { void M(); int R { get; } }");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var e = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E"));
        Assert.Equal("class", e.ContainerKind);
        Assert.Equal("C", e.ContainerName);
        Assert.Equal("public event System.EventHandler E;", e.Signature);

        var p = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("class", p.ContainerKind);
        Assert.Equal("C", p.ContainerName);
        Assert.Equal("public int P { get; set; }", p.Signature);

        var q = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Q"));
        Assert.Equal("struct", q.ContainerKind);
        Assert.Equal("S", q.ContainerName);
        Assert.Equal("public int Q { get; set; }", q.Signature);

        var f = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "F"));
        Assert.Equal("struct", f.ContainerKind);
        Assert.Equal("S", f.ContainerName);
        Assert.Equal("public event System.EventHandler F;", f.Signature);

        var m = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "M"));
        Assert.Equal("interface", m.ContainerKind);
        Assert.Equal("I", m.ContainerName);
        Assert.Equal("void M();", m.Signature);

        var r = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "R"));
        Assert.Equal("interface", r.ContainerKind);
        Assert.Equal("I", r.ContainerName);
        Assert.Equal("int R { get; }", r.Signature);
    }

    [Fact]
    public void Extract_CSharp_BodylessMembersWithTrailingCommentsStopAtSemicolon()
    {
        // Semicolon-terminated C# members must stop at the semicolon even when a trailing
        // line or block comment follows it. Otherwise the range scan walks into the next
        // brace body and corrupts `end_line` / `body_end_line` for the last body-less member
        // in a block. Closes #245.
        // `;` で終わる C# の body-less member は、その後ろに行コメント / ブロックコメントが
        // 続いても semicolon で範囲を止める必要がある。そうしないと range scan が次の
        // brace body まで進み、block 内の最後の body-less member の `end_line` / `body_end_line`
        // が壊れる。Closes #245.
        var content = """
            namespace Demo;

            public interface IFoo
            {
                int A();                     // trailing line comment
                int B(int x); // another trailing line comment
                int C();                     // no real body here either
                int D(int y); /* trailing block comment */
            }

            public class X
            {
                public int DoIt() => 42;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var d = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "D"));
        Assert.Equal(8, d.StartLine);
        Assert.Equal(8, d.EndLine);
        Assert.Null(d.BodyStartLine);
        Assert.Null(d.BodyEndLine);

        var x = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "X"));
        Assert.Equal(11, x.StartLine);
        Assert.Equal(14, x.EndLine);
    }

    [Fact]
    public void Extract_CSharp_SameLineAccessorEventsStillExposeSiblingMembers()
    {
        // Accessor-based same-line events must clamp their signature at the accessor body
        // and still reopen earlier patterns for later siblings. Without that brace-end
        // clamp, `event E { add {} remove {} } public int P { get; set; }` stores the
        // event signature through the property and silently drops `P`. Also pin the
        // reverse `property + accessor event` order, including a generic event type with
        // internal whitespace, so both sibling directions remain visible. Closes #520.
        // 同一行の accessor event は accessor 本体の閉じ `}` で signature を切り、
        // 後続 sibling のために earlier pattern を再び開く必要がある。そうしないと
        // `event E { add {} remove {} } public int P { get; set; }` で event signature が
        // property まで飲み込み、`P` が無言で欠落する。逆順の `property + accessor event`
        // も、空白入り generic event 型を含めて固定し、両方向の sibling が可視なまま
        // であることを保証する。Closes #520.
        var content = string.Join(
            "\n",
            "public class C { public event System.EventHandler E { add {} remove {} } public int P { get; set; } }",
            "public struct S { public int Q { get; set; } public event System.Action<int, string> F { add {} remove {} } }");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var e = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E"));
        Assert.Equal("class", e.ContainerKind);
        Assert.Equal("C", e.ContainerName);
        Assert.Equal("public event System.EventHandler E { add {} remove {} }", e.Signature);

        var p = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("class", p.ContainerKind);
        Assert.Equal("C", p.ContainerName);
        Assert.Equal("public int P { get; set; }", p.Signature);

        var q = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Q"));
        Assert.Equal("struct", q.ContainerKind);
        Assert.Equal("S", q.ContainerName);
        Assert.Equal("public int Q { get; set; }", q.Signature);

        var f = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "F"));
        Assert.Equal("struct", f.ContainerKind);
        Assert.Equal("S", f.ContainerName);
        Assert.Equal("public event System.Action<int, string> F { add {} remove {} }", f.Signature);
    }

    [Fact]
    public void Extract_CSharp_SameLineGenericBraceBodiedMembersStillExposeLaterCompactSiblings()
    {
        // After the #525 raw-column brace clamp, same-line generic brace-bodied
        // members must translate their sibling-restart offset back into the
        // collapsed match-line column domain before reopening the pattern scan.
        // Otherwise `M<T1,           T2>() { }int P { get; }event ... E;`
        // restarts too far to the right, drops `P` / `E`, or lets `M` absorb the
        // following sibling text. Closes #533.
        // #525 の raw-column brace clamp 後は、same-line の generic brace-body member が
        // sibling scan を再開する位置を、pattern scan 再開前に collapsed match-line 側の
        // 列空間へ戻す必要がある。そうしないと
        // `M<T1,           T2>() { }int P { get; }event ... E;` で再開位置が右にずれ、
        // `P` / `E` が欠落するか、`M` の signature が後続 sibling を飲み込む。Closes #533.
        var content = "public interface I { void M<T1,           T2>() { }int P { get; }event System.Action<int,           string> E; }\n";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var m = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "M"));
        Assert.Equal("interface", m.ContainerKind);
        Assert.Equal("I", m.ContainerName);
        Assert.Equal("void M<T1,           T2>() { }", m.Signature);

        var p = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("interface", p.ContainerKind);
        Assert.Equal("I", p.ContainerName);
        Assert.Equal("int P { get; }", p.Signature);

        var e = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E"));
        Assert.Equal("interface", e.ContainerKind);
        Assert.Equal("I", e.ContainerName);
        Assert.Equal("event System.Action<int,           string> E;", e.Signature);
    }

    [Fact]
    public void Extract_CSharp_GenericSameLineSemicolonMembersKeepTerminator()
    {
        // The same-line semicolon-boundary fix for #473 must translate collapsed generic
        // columns back to raw columns before slicing signatures, or spaces inside generic
        // arguments make the extracted signature stop one character early and silently drop
        // the terminating `;`. Lock event / interface-method / delegate shapes that all
        // route through the semicolon-body path. Closes #473 review follow-up.
        // #473 の same-line semicolon 境界 fix は、signature を切り出す前に collapsed
        // generic 列を raw 列へ戻す必要がある。そうしないと generic 引数内の空白のぶんだけ
        // signature が 1 文字短くなり、終端 `;` が無言で脱落する。semicolor-body 経路を
        // 通る event / interface method / delegate の 3 形を固定する。Closes #473 review
        // follow-up.
        var content = string.Join(
            "\n",
            "public class C { public event System.Action<int, string> E; public int P { get; set; } }",
            "public interface I { void M<T1, T2>(); int R { get; } }",
            "public class Holder { public delegate void Inner<T1, T2>(); public int Q { get; set; } }");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var e = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E"));
        Assert.Equal("public event System.Action<int, string> E;", e.Signature);
        Assert.Equal("class", e.ContainerKind);
        Assert.Equal("C", e.ContainerName);

        var m = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "M"));
        Assert.Equal("void M<T1, T2>();", m.Signature);
        Assert.Equal("interface", m.ContainerKind);
        Assert.Equal("I", m.ContainerName);

        var inner = Assert.Single(symbols.Where(s => s.Kind == "delegate" && s.Name == "Inner"));
        Assert.Equal("public delegate void Inner<T1, T2>();", inner.Signature);
        Assert.Equal("class", inner.ContainerKind);
        Assert.Equal("Holder", inner.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_SameLineSemicolonMembersClampRangeAtTopLevelSemicolon()
    {
        // Body-less C# members (`void M();`, `event E;`, `delegate D();`) on the same
        // physical line as the enclosing type's closing `}` must clamp their range at
        // the in-line `;`. Before this fix, FindCSharpBraceRange only short-circuited
        // when the entire scan line ended with `;`, so a single-member interface
        // `interface I { void M(); }` and the property->method order
        // `interface J { int P { get; } void M(); }` both bled past the `}` into the
        // next file line, attributing the next type's brace range to M and emitting
        // wrong end_line / body_start_line / body_end_line. Closes #515.
        // 同じ物理行に外側型の閉じ `}` がある body-less な C# member
        // (`void M();`, `event E;`, `delegate D();`) は、行内 `;` の時点で範囲を確定
        // させなければならない。修正前の FindCSharpBraceRange は scan 行末が `;` で
        // 終わる場合だけ早期 return していたため、単一メンバー interface
        // `interface I { void M(); }` や property->method 並びの
        // `interface J { int P { get; } void M(); }` がいずれも `}` を越えてファイル
        // 次行に食い込み、次の型の brace 範囲を M に帰属させて end_line /
        // body_start_line / body_end_line を誤らせていた。Closes #515.
        var content = string.Join(
            "\n",
            "public interface I { void M(); }",
            "public interface J { int P { get; } void M(); }",
            "public interface K { void M(); int P { get; } }",
            "public class L { public int P { get; set; } public event System.EventHandler E; }");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var solitaryMethod = Assert.Single(symbols.Where(s =>
            s.Kind == "function"
            && s.Name == "M"
            && s.ContainerKind == "interface"
            && s.ContainerName == "I"));
        Assert.Equal(1, solitaryMethod.Line);
        Assert.Equal(1, solitaryMethod.StartLine);
        Assert.Equal(1, solitaryMethod.EndLine);
        Assert.Null(solitaryMethod.BodyStartLine);
        Assert.Null(solitaryMethod.BodyEndLine);
        Assert.Equal("void M();", solitaryMethod.Signature);

        var afterPropertyMethod = Assert.Single(symbols.Where(s =>
            s.Kind == "function"
            && s.Name == "M"
            && s.ContainerKind == "interface"
            && s.ContainerName == "J"));
        Assert.Equal(2, afterPropertyMethod.Line);
        Assert.Equal(2, afterPropertyMethod.StartLine);
        Assert.Equal(2, afterPropertyMethod.EndLine);
        Assert.Null(afterPropertyMethod.BodyStartLine);
        Assert.Null(afterPropertyMethod.BodyEndLine);
        Assert.Equal("void M();", afterPropertyMethod.Signature);

        // Method-then-property order keeps existing behavior: M still has no body
        // metadata leak from the trailing property's brace range.
        // method-then-property 並びでも、後続 property の brace 範囲が M の body
        // メタデータに混入しないことを確認する。
        var beforePropertyMethod = Assert.Single(symbols.Where(s =>
            s.Kind == "function"
            && s.Name == "M"
            && s.ContainerKind == "interface"
            && s.ContainerName == "K"));
        Assert.Equal(3, beforePropertyMethod.Line);
        Assert.Equal(3, beforePropertyMethod.StartLine);
        Assert.Equal(3, beforePropertyMethod.EndLine);
        Assert.Null(beforePropertyMethod.BodyStartLine);
        Assert.Null(beforePropertyMethod.BodyEndLine);
        Assert.Equal("void M();", beforePropertyMethod.Signature);

        // Same fix also locks event range when a property accessor block precedes it
        // on the same line (the original #473 case is regressed via the same path).
        // 同じ修正は、property accessor block を先行させた event 並びでも range を
        // 固定する (#473 元ケースも同じ経路で reg している)。
        var trailingEvent = Assert.Single(symbols.Where(s =>
            s.Kind == "event"
            && s.Name == "E"
            && s.ContainerKind == "class"
            && s.ContainerName == "L"));
        Assert.Equal(4, trailingEvent.Line);
        Assert.Equal(4, trailingEvent.StartLine);
        Assert.Equal(4, trailingEvent.EndLine);
        Assert.Null(trailingEvent.BodyStartLine);
        Assert.Null(trailingEvent.BodyEndLine);
        Assert.Equal("public event System.EventHandler E;", trailingEvent.Signature);
    }

    [Fact]
    public void Extract_CSharp_GenericSameLineMembersKeepLaterBraceSiblingStartColumns()
    {
        // After earlier generic same-line members collapse whitespace in the per-line
        // C# match buffer, later brace-bodied siblings must still slice their signature
        // from the raw start column. Otherwise `int P { get; }` and `interface J { ... }`
        // keep the preceding `;` / `{` from the raw line even though the symbols
        // themselves are found. Pin both the direct property case and the nested
        // interface case reported in #525. Closes #525.
        // 先行する generic な same-line member によって C# の per-line match buffer 側で
        // 空白が潰れても、後続 brace-bodied sibling の signature は raw start 列から
        // 切り出されなければならない。そうでないと `int P { get; }` や
        // `interface J { ... }` の先頭に、raw 行上の直前 delimiter (`;` / `{`) が残る。
        // symbol 自体は見つかっていても signature が壊れるので、#525 の direct
        // property ケースと nested interface ケースの両方を固定する。Closes #525.
        var content = string.Join(
            "\n",
            "public interface I { void M<T1, T2>(); event System.Action<int, string> E; int P { get; } }",
            "public interface I2 { void M<T1, T2>(); event System.Action<int, string> E; interface J { int P { get; } } }");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var directProperty = Assert.Single(symbols.Where(s =>
            s.Kind == "property"
            && s.Name == "P"
            && s.ContainerKind == "interface"
            && s.ContainerName == "I"));
        Assert.Equal("int P { get; }", directProperty.Signature);

        var nestedInterface = Assert.Single(symbols.Where(s =>
            s.Kind == "interface"
            && s.Name == "J"
            && s.ContainerKind == "interface"
            && s.ContainerName == "I2"));
        Assert.Equal("interface J { int P { get; } }", nestedInterface.Signature);

        var nestedProperty = Assert.Single(symbols.Where(s =>
            s.Kind == "property"
            && s.Name == "P"
            && s.Line == 2
            && s.Signature == "int P { get; }"));
        Assert.Equal("int P { get; }", nestedProperty.Signature);
    }

    [Fact]
    public void Extract_CSharp_SameLineNestedInterfacePropertyUsesInnermostContainer()
    {
        // Same-line nested interface members must stay attached to the nested interface,
        // even when earlier same-line siblings are longer and would otherwise reorder the
        // container walk by signature length. Before #529, `P` attached to outer `I2`
        // because `AssignContainers` processed same-line symbols out of source order and
        // popped `J` before reaching the later property. Closes #529.
        // 同一行の nested interface member は、先行 sibling の signature 長によって
        // same-line の処理順が崩れても、外側 `I2` ではなく内側 `J` に属し続ける必要が
        // ある。#529 前は `AssignContainers` が source order を失い、後続 property に
        // 到達する前に `J` を stack から外してしまうため `P` が `I2` に誤帰属していた。
        const string content = "public interface I2 { void M<T1, T2>(); event System.Action<int, string> E; interface J { int P { get; } } }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var nestedInterface = Assert.Single(symbols.Where(s => s.Kind == "interface" && s.Name == "J"));
        Assert.Equal("interface", nestedInterface.ContainerKind);
        Assert.Equal("I2", nestedInterface.ContainerName);

        var nestedProperty = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("int P { get; }", nestedProperty.Signature);
        Assert.Equal("interface", nestedProperty.ContainerKind);
        Assert.Equal("J", nestedProperty.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_GenericBraceMembersRestartCollapsedSameLineSiblingsFromCollapsedColumns()
    {
        // Raw-column brace fixes for same-line generic members must not leak into the
        // sibling restart offset. The restart scan still runs on the collapsed C#
        // match line, so using the raw closing-brace column directly can jump into/past
        // a later compact sibling when generic whitespace was removed earlier in the
        // line. Pin the no-space compact chain where `M<T1,           T2>() { }int P`
        // used to lose both `P` and `E` and absorb `P` into `M`'s signature. Closes #533.
        // same-line generic member の raw-column brace fix は、sibling 再開位置まで
        // raw 列のまま漏れてはいけない。再開スキャン自体は collapsed な C# match 行
        // 上で動くため、閉じ brace の raw 列をそのまま使うと、generic 内で先に消えた
        // 空白ぶんだけ次の compact sibling の途中/後ろへ飛んでしまう。`M<T1, T2>() { }int P`
        // 形で `P` / `E` が落ち、`M` の signature が `P` を飲み込んでいた回帰を固定する。
        // Closes #533.
        const string content = "public interface I { void M<T1,           T2>() { }int P { get; }event System.Action<int,           string> E; }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var method = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "M"));
        Assert.Equal("void M<T1,           T2>() { }", method.Signature);
        Assert.Equal("interface", method.ContainerKind);
        Assert.Equal("I", method.ContainerName);

        var property = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("int P { get; }", property.Signature);
        Assert.Equal("interface", property.ContainerKind);
        Assert.Equal("I", property.ContainerName);

        var evt = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E"));
        Assert.Equal("event System.Action<int,           string> E;", evt.Signature);
        Assert.Equal("interface", evt.ContainerKind);
        Assert.Equal("I", evt.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_WrappedNestedClassMembersStayAttachedToNestedContainer()
    {
        // The #525 wrapped-header signature fix must not evict a same-line nested type
        // from later container assignment. In the regression from #535, `Wrapped` kept
        // its corrected multi-line header signature, but the later property `P` attached
        // to the outer `Host` class after same-line siblings popped `Wrapped` out of the
        // active container stack too early. Pin the exact issue fixture so both the
        // signature fix and the nested-container attachment stay true together. Closes #535.
        // #525 の wrapped-header signature fix は、同一行にいる nested type を後続 member の
        // container 判定から追い出してはならない。#535 の回帰では `Wrapped` の multi-line
        // header 自体は正しくなった一方で、同一行 sibling を処理した時点で active container
        // stack から `Wrapped` が早く落ち、後続 property `P` が outer `Host` に付いていた。
        // issue の fixture そのものを固定し、signature 修正と nested-container 付与が同時に
        // 崩れないようにする。Closes #535.
        var content = string.Join(
            "\n",
            "namespace ReviewFixtures;",
            "",
            "public class Host",
            "{",
            "    public void M<T1, T2>() { } public event System.Action<int, string>? E; public class Wrapped<T>",
            "        where T : class",
            "    {",
            "        public int P { get; }",
            "    }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var wrapped = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Wrapped"));
        Assert.Equal("public class Wrapped<T> where T : class", wrapped.Signature);
        Assert.Equal("class", wrapped.ContainerKind);
        Assert.Equal("Host", wrapped.ContainerName);

        var property = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("public int P { get; }", property.Signature);
        Assert.Equal("class", property.ContainerKind);
        Assert.Equal("Wrapped", property.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_WrappedNestedPartialTypesKeepRootToLeafFamilyKeys()
    {
        // The #535 container-path fix changes `BuildSelfFamilyKey` to receive a root-to-leaf
        // effective container path. If the old `Reverse()` is left in place, nested partial
        // types flip their family key order (`Host.ReviewFixtures.Wrapped`) and no longer
        // match the container-qualified-name contract used by hotspot-family grouping.
        // Pin a wrapped nested partial-type fixture so both the container path and family key
        // stay in canonical root-to-leaf order. Closes #541.
        // #535 の container-path fix 以降、`BuildSelfFamilyKey` は root-to-leaf 順の
        // effective container path を受け取る。ここで旧 `Reverse()` が残ると、nested partial
        // type の family key が `Host.ReviewFixtures.Wrapped` のように逆順化し、
        // hotspot-family grouping が依存する container-qualified-name 契約と食い違う。
        // wrapped な nested partial-type の fixture を固定し、container path と family key の
        // 両方が canonical な root-to-leaf 順を保つことを検証する。Closes #541.
        var content = string.Join(
            "\n",
            "namespace ReviewFixtures;",
            "",
            "public class Host",
            "{",
            "    public void M<T1, T2>() { } public event System.Action<int, string>? E; public partial class Wrapped<T>",
            "        where T : class",
            "    {",
            "        public partial class Child",
            "        {",
            "        }",
            "    }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var wrapped = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Wrapped"));
        Assert.Equal("ReviewFixtures.Host", wrapped.ContainerQualifiedName);
        Assert.Equal("ReviewFixtures.Host.Wrapped", wrapped.FamilyKey);

        var child = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Child"));
        Assert.Equal("ReviewFixtures.Host.Wrapped", child.ContainerQualifiedName);
        Assert.Equal("ReviewFixtures.Host.Wrapped", child.FamilyKey);
    }

    [Fact]
    public void Extract_CSharp_WrappedNestedTypeClosingBraceLineStillFindsOuterSibling()
    {
        // Wrapped multi-line nested types can end on a line that also starts a later outer
        // sibling (`} public int Q { get; }`). The line-level C# scan must restart from the
        // post-brace statement boundary instead of treating the leading `}` as a dead line,
        // or the outer sibling silently disappears from symbols/definition/outline. Closes #545.
        // 折り返された multi-line nested type は、閉じ brace 行に outer sibling が続く
        // (`} public int Q { get; }`) 形を取りうる。C# の行単位 scan は先頭 `}` の後ろにある
        // statement 境界から再開しなければならず、そうしないと outer sibling が
        // symbols/definition/outline から無言で脱落する。Closes #545.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public partial class Host",
            "{",
            "    public partial class Wrapped<T>",
            "        where T : class",
            "    {",
            "        public int P { get; }",
            "    } public int Q { get; }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var wrapped = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Wrapped"));
        Assert.Equal("class", wrapped.ContainerKind);
        Assert.Equal("Host", wrapped.ContainerName);

        var innerProperty = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("class", innerProperty.ContainerKind);
        Assert.Equal("Wrapped", innerProperty.ContainerName);

        var outerProperty = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Q"));
        Assert.Equal("public int Q { get; }", outerProperty.Signature);
        Assert.Equal("class", outerProperty.ContainerKind);
        Assert.Equal("Host", outerProperty.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_WrappedNestedTypeClosingBraceLineKeepsLastInnerMemberAndOuterSibling()
    {
        // A wrapped nested type can end on the same line as both its last inner member and
        // a later outer sibling (`public int P { get; } } public int Q { get; }`). The
        // closing-line body clamp from #545 must still keep `P` inside `Wrapped`, while the
        // same-line restart skips the intervening `}` and still reaches outer sibling `Q`.
        // Closes #549.
        // wrapped な nested type は、最後の inner member と後続 outer sibling が同じ閉じ
        // brace 行 (`public int P { get; } } public int Q { get; }`) に載ることがある。
        // #545 の closing-line body clamp は `P` を `Wrapped` の内側に残しつつ、same-line
        // restart は間の `}` を飛ばして outer sibling `Q` に到達しなければならない。
        // Closes #549.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public partial class Host",
            "{",
            "    public partial class Wrapped<T>",
            "        where T : class",
            "    {",
            "        public int P { get; } } public int Q { get; }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var innerProperty = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("class", innerProperty.ContainerKind);
        Assert.Equal("Wrapped", innerProperty.ContainerName);

        var outerProperty = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Q"));
        Assert.Equal("public int Q { get; }", outerProperty.Signature);
        Assert.Equal("class", outerProperty.ContainerKind);
        Assert.Equal("Host", outerProperty.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_WrappedNestedTypeBraceBodiedLastInnerMemberKeepsOuterPropertySibling()
    {
        // A compact brace-bodied inner type can sit on the wrapped type's closing-brace line
        // before an outer property sibling (`Child { } } public int Q { get; }`). The
        // type-header false-positive recovery must skip the intermediate `}` and still
        // reach `Q`, while keeping `Child` attached to `Wrapped`. Closes #554.
        // compact な brace-bodied inner type は、wrapped type の closing-brace 行で outer
        // property sibling (`Child { } } public int Q { get; }`) の直前に現れうる。type
        // header 偽陽性からの same-line 再開は中間の `}` を飛ばして `Q` まで届きつつ、
        // `Child` を `Wrapped` 配下に残さなければならない。Closes #554.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public partial class Host",
            "{",
            "    public partial class Wrapped<T>",
            "        where T : class",
            "    {",
            "        public partial class Child { } } public int Q { get; }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var child = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Child"));
        Assert.Equal("class", child.ContainerKind);
        Assert.Equal("Wrapped", child.ContainerName);

        var outerProperty = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Q"));
        Assert.Equal("public int Q { get; }", outerProperty.Signature);
        Assert.Equal("class", outerProperty.ContainerKind);
        Assert.Equal("Host", outerProperty.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_WrappedNestedTypeBraceBodiedLastInnerMemberKeepsOuterTypeSibling()
    {
        // The same compact `Child { } }` shape must also keep a later outer class sibling
        // visible. Otherwise the type-header recovery restarts on the closing `}` and drops
        // `Sibling` from symbol-oriented queries. Closes #554.
        // 同じ compact な `Child { } }` 形では、後続の outer class sibling も見え続けなければ
        // ならない。そうでないと type-header 回復が closing `}` から再開して `Sibling` を
        // symbol 系クエリから落としてしまう。Closes #554.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public partial class Host",
            "{",
            "    public partial class Wrapped<T>",
            "        where T : class",
            "    {",
            "        public partial class Child { } } public partial class Sibling",
            "        {",
            "        }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var child = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Child"));
        Assert.Equal("class", child.ContainerKind);
        Assert.Equal("Wrapped", child.ContainerName);

        var sibling = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Sibling"));
        Assert.Equal("class", sibling.ContainerKind);
        Assert.Equal("Host", sibling.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_EmptySameLineNestedInterfaceBodyKeepsLaterOuterClassSibling()
    {
        // An empty same-line nested interface body must not consume a later outer class
        // sibling of a different kind. The #585 repro previously emitted `Outer` + `I`
        // but dropped `Sibling` after the interface's compact `{ }` body. Closes #585.
        // 空の same-line nested interface body は、kind が異なる後続 outer class sibling を
        // 飲み込んではならない。#585 の repro では、以前は interface の compact な `{ }`
        // 本体の後で `Outer` と `I` だけが出力され、`Sibling` が落ちていた。Closes #585.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "[A]",
            "public class Outer { public interface I<T1,           T2> { } public class Sibling { } }");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var outer = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Outer"));
        Assert.Equal("namespace", outer.ContainerKind);
        Assert.Equal("Demo", outer.ContainerName);

        var nestedInterface = Assert.Single(symbols.Where(s => s.Kind == "interface" && s.Name == "I"));
        Assert.Equal("class", nestedInterface.ContainerKind);
        Assert.Equal("Outer", nestedInterface.ContainerName);

        var sibling = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Sibling"));
        Assert.Equal("class", sibling.ContainerKind);
        Assert.Equal("Outer", sibling.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_WrappedEmptySameLineNestedTypeBodyKeepsLaterOuterPropertySibling()
    {
        // A wrapped nested type whose last inner member is `Child { } }` must still leave a
        // later outer property visible on that same closing-brace line. The #585 repro
        // previously kept `Child` but dropped `P` from the outer `Host`. Closes #585.
        // wrapped nested type の最後の inner member が `Child { } }` でも、同じ closing-brace
        // 行に続く outer property は見え続けなければならない。#585 の repro では、以前は
        // `Child` は残る一方で outer `Host` の `P` が落ちていた。Closes #585.
        var content = string.Join(
            "\n",
            "public class Host",
            "{",
            "    public class Wrapped<T>",
            "        where T : class",
            "    {",
            "        public class Child { } } public int P { get; set; }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var child = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Child"));
        Assert.Equal("class", child.ContainerKind);
        Assert.Equal("Wrapped", child.ContainerName);

        var property = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("class", property.ContainerKind);
        Assert.Equal("Host", property.ContainerName);
        Assert.Equal("public int P { get; set; }", property.Signature);
    }

    [Fact]
    public void Extract_CSharp_WrappedNestedTypeKeepsSameLineDuplicateSignatureSiblings()
    {
        // Duplicate suppression must not collapse distinct same-line siblings just because
        // they share the same short signature before container assignment runs. In the
        // compact wrapped case below, both `Child` declarations are real: one inside
        // `Wrapped`, one later under `Host`. Closes #552.
        // duplicate suppression は、container 判定前に短い signature が一致するだけで
        // 別物の same-line sibling を潰してはならない。下の compact な wrapped case では
        // `Child` 宣言が 2 つとも実在し、片方は `Wrapped` 配下、もう片方は後続の `Host`
        // 配下である。Closes #552.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public partial class Host",
            "{",
            "    public partial class Wrapped<T>",
            "        where T : class",
            "    {",
            "        public partial class Child { } } public partial class Child { }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var children = symbols
            .Where(s => s.Kind == "class" && s.Name == "Child")
            .OrderBy(s => s.ContainerName)
            .ToList();
        Assert.Equal(2, children.Count);

        Assert.Contains(children, child => child.ContainerKind == "class" && child.ContainerName == "Wrapped");
        Assert.Contains(children, child => child.ContainerKind == "class" && child.ContainerName == "Host");
    }

    [Fact]
    public void Extract_CSharp_WrappedNestedTypeIgnoresStringLiteralLookalikesWhenTrackingSameLineOccurrences()
    {
        // Same-line occurrence tracking must ignore string-literal lookalikes of the same
        // declaration signature. Otherwise the later real declaration is mapped onto the
        // quoted copy and the outer sibling misattaches under the inner `Child`. Closes #558.
        // same-line occurrence tracking は、同じ宣言 signature を含む文字列リテラルを
        // 数えてはいけない。数えてしまうと後続の本物の宣言が quoted copy に対応付けられ、
        // outer sibling が inner `Child` 配下へ誤帰属する。Closes #558.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public partial class Host",
            "{",
            "    public partial class Wrapped<T>",
            "        where T : class",
            "    {",
            "        public const string Marker = \"public partial class Child { }\"; public partial class Child { } } public partial class Child { }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var children = symbols
            .Where(s => s.Kind == "class" && s.Name == "Child")
            .OrderBy(s => s.ContainerName)
            .ToList();
        Assert.Equal(2, children.Count);

        Assert.Contains(children, child => child.ContainerKind == "class" && child.ContainerName == "Wrapped");
        Assert.Contains(children, child => child.ContainerKind == "class" && child.ContainerName == "Host");
    }

    [Fact]
    public void Extract_CSharp_WrappedNestedTypeIgnoresMultilineCommentLookalikesWhenTrackingSameLineOccurrences()
    {
        // Same-line occurrence recovery must also respect block-comment state carried from
        // earlier lines. Otherwise a declaration-shaped lookalike inside a multiline comment
        // consumes the first occurrence slot and the later outer sibling is misattached under
        // the inner wrapped type. Closes #567.
        // same-line occurrence 復元は、前行から継続する block comment state も尊重しなければ
        // ならない。そうしないと複数行コメント中の宣言風 lookalike が最初の occurrence slot を
        // 奪い、後続 outer sibling が inner wrapped type 配下へ誤帰属する。Closes #567.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public partial class Host",
            "{",
            "    public partial class Wrapped<T>",
            "        where T : class",
            "    {",
            "        /*",
            "        public partial class Child { } */ public partial class Child { } } public partial class Child { }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var children = symbols
            .Where(s => s.Kind == "class" && s.Name == "Child")
            .OrderBy(s => s.ContainerName)
            .ToList();
        Assert.Equal(2, children.Count);

        Assert.Contains(children, child => child.ContainerKind == "class" && child.ContainerName == "Wrapped");
        Assert.Contains(children, child => child.ContainerKind == "class" && child.ContainerName == "Host");
    }

    [Fact]
    public void Extract_CSharp_CarriedVerbatimStringContinuationStillFindsLaterSameLineNestedAndOuterTypes()
    {
        // When a physical line begins inside a carried verbatim string, the closing `";`
        // leaves a top-level semicolon before the real declaration stream. The same-line
        // C# restart must skip that empty statement and still reach the later real types.
        // Closes #630 / #633.
        // 継続中の verbatim string から始まる物理行では、閉じ `";` の直後に top-level の
        // 空文 `;` が残る。same-line の C# 再開はその空文を飛ばし、後続の実型宣言まで
        // 到達しなければならない。Closes #630 / #633.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public partial class Host",
            "{",
            "    public partial class Wrapped<T>",
            "        where T : class",
            "    {",
            "        private string _s = @\"",
            "        public partial class Fake { }\"; public partial class Child { } } public partial class OuterChild { }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Fake");
        var child = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Child"));
        Assert.Equal("class", child.ContainerKind);
        Assert.Equal("Wrapped", child.ContainerName);
        Assert.Equal("public partial class Child { }", child.Signature);

        var outerChild = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "OuterChild"));
        Assert.Equal("class", outerChild.ContainerKind);
        Assert.Equal("Host", outerChild.ContainerName);
        Assert.Equal("public partial class OuterChild { }", outerChild.Signature);
    }

    [Fact]
    public void Extract_CSharp_CarriedRawStringContinuationStillFindsLaterSameLineNestedAndOuterTypes()
    {
        // Raw-string continuation lines have the same top-level `;` restart hazard as
        // verbatim strings. The fake declaration inside the string must stay suppressed
        // while the later real nested and outer siblings still extract. Closes #630 / #633.
        // raw string の継続行も、verbatim string と同じく top-level の `;` 再開ハザードを持つ。
        // 文字列内の fake 宣言は抑止したまま、後続の実 nested / outer sibling を抽出する必要がある。
        // Closes #630 / #633.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public partial class Host",
            "{",
            "    public partial class Wrapped<T>",
            "        where T : class",
            "    {",
            "        private string _s = \"\"\"",
            "        public partial class Fake { }\"\"\"; public partial class Child { } } public partial class OuterChild { }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Fake");
        var child = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Child"));
        Assert.Equal("class", child.ContainerKind);
        Assert.Equal("Wrapped", child.ContainerName);
        Assert.Equal("public partial class Child { }", child.Signature);

        var outerChild = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "OuterChild"));
        Assert.Equal("class", outerChild.ContainerKind);
        Assert.Equal("Host", outerChild.ContainerName);
        Assert.Equal("public partial class OuterChild { }", outerChild.Signature);
    }

    [Fact]
    public void Extract_CSharp_CarriedVerbatimStringContinuationPreservesSameLinePropertySiblings()
    {
        // A carried verbatim-string close line must clamp the first same-line brace-bodied
        // property at its real `}` so later property siblings on the same physical line
        // still restart and extract independently. Closes #636.
        // 継続中の verbatim string の close line では、同一物理行上の最初の brace-body
        // property を本物の `}` で切り、後続 property sibling が再開して独立抽出される
        // 必要がある。Closes #636.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public class C",
            "{",
            "    private string _s = @\"",
            "    fake\"; public int P { get; } public int Q { get; }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var propertyP = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("public int P { get; }", propertyP.Signature);
        Assert.Equal("C", propertyP.ContainerName);

        var propertyQ = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Q"));
        Assert.Equal("public int Q { get; }", propertyQ.Signature);
        Assert.Equal("C", propertyQ.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_CarriedRawStringContinuationPreservesSameLinePropertySiblings()
    {
        // Raw-string continuation lines share the same brace-end clamp requirement as
        // verbatim strings: the first same-line property must stop at its own accessor
        // block so later property siblings remain visible. Closes #636.
        // raw string の継続行も、verbatim string と同じ brace-end clamp を必要とする。
        // 先頭 property は自分の accessor block で止まり、後続 property sibling が
        // 見えるままでなければならない。Closes #636.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public class C",
            "{",
            "    private string _s = \"\"\"",
            "    fake\"\"\"; public int P { get; } public int Q { get; }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var propertyP = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("public int P { get; }", propertyP.Signature);
        Assert.Equal("C", propertyP.ContainerName);

        var propertyQ = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Q"));
        Assert.Equal("public int Q { get; }", propertyQ.Signature);
        Assert.Equal("C", propertyQ.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_CarriedVerbatimStringContinuationPreservesSameLineMethodSiblings()
    {
        // Carried verbatim-string close lines must also clamp same-line brace-bodied
        // methods. Otherwise the first method absorbs the rest of the line and later
        // siblings disappear. Closes #636.
        // 継続中の verbatim string の close line では、同一行 brace-body method も
        // 正しく切り出す必要がある。そうしないと先頭 method が残り全体を飲み込み、
        // 後続 sibling が消える。Closes #636.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public class C",
            "{",
            "    private string _s = @\"",
            "    fake\"; public void M() { } public void N() { }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var methodM = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "M"));
        Assert.Equal("public void M() { }", methodM.Signature);
        Assert.Equal("C", methodM.ContainerName);

        var methodN = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "N"));
        Assert.Equal("public void N() { }", methodN.Signature);
        Assert.Equal("C", methodN.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_CarriedRawStringContinuationPreservesSameLineMethodSiblings()
    {
        // Raw-string continuation lines need the same method-body clamp: both methods on
        // the close line must survive with their own signatures instead of collapsing into
        // one oversized match. Closes #636.
        // raw string の継続行でも method-body clamp は同じく必要であり、close line 上の
        // 2 つの method は 1 つの過大 signature に潰れず、それぞれ独立して残る
        // 必要がある。Closes #636.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public class C",
            "{",
            "    private string _s = \"\"\"",
            "    fake\"\"\"; public void M() { } public void N() { }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var methodM = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "M"));
        Assert.Equal("public void M() { }", methodM.Signature);
        Assert.Equal("C", methodM.ContainerName);

        var methodN = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "N"));
        Assert.Equal("public void N() { }", methodN.Signature);
        Assert.Equal("C", methodN.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_CarriedVerbatimStringContinuationWithSameLineAccessorEventStillFindsLaterTypes()
    {
        // Carried verbatim close-lines can now restart across the top-level `";`, but
        // same-line accessor-event sibling recovery must also use the carried lexical
        // state or the later nested/outer class declarations still disappear.
        // Closes #630 / #633 follow-up.
        // 継続 verbatim string の close-line では top-level の `";` を跨いで再開できても、
        // same-line accessor event の sibling 回復も carried lexical state を使わないと
        // 後続の nested / outer class 宣言がまだ消えてしまう。Closes #630 / #633 follow-up.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public partial class Host",
            "{",
            "    public partial class Wrapped<T>",
            "        where T : class",
            "    {",
            "        private string _s = @\"",
            "        public partial class Fake { }\"; public event System.Action E { add { } remove { } } public partial class Child { } } public partial class OuterChild { }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Fake");

        var evt = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E"));
        Assert.Equal("Wrapped", evt.ContainerName);
        Assert.Equal("public event System.Action E { add { } remove { } }", evt.Signature);

        var child = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Child"));
        Assert.Equal("Wrapped", child.ContainerName);
        Assert.Equal("public partial class Child { }", child.Signature);

        var outerChild = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "OuterChild"));
        Assert.Equal("Host", outerChild.ContainerName);
        Assert.Equal("public partial class OuterChild { }", outerChild.Signature);
    }

    [Fact]
    public void Extract_CSharp_CarriedRawStringContinuationWithSameLineAccessorEventStillFindsLaterTypes()
    {
        // Raw-string close-lines should keep the same event-sibling restart contract as the
        // verbatim path: same-line accessor events must not block later nested/outer types.
        // Closes #630 / #633 follow-up.
        // raw string の close-line でも verbatim と同じ event-sibling 再開契約を保ち、
        // same-line accessor event が後続の nested / outer type を塞がないことを確認する。
        // Closes #630 / #633 follow-up.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public partial class Host",
            "{",
            "    public partial class Wrapped<T>",
            "        where T : class",
            "    {",
            "        private string _s = \"\"\"",
            "        public partial class Fake { }\"\"\"; public event System.Action E { add { } remove { } } public partial class Child { } } public partial class OuterChild { }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "Fake");

        var evt = Assert.Single(symbols.Where(s => s.Kind == "event" && s.Name == "E"));
        Assert.Equal("Wrapped", evt.ContainerName);
        Assert.Equal("public event System.Action E { add { } remove { } }", evt.Signature);

        var child = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Child"));
        Assert.Equal("Wrapped", child.ContainerName);
        Assert.Equal("public partial class Child { }", child.Signature);

        var outerChild = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "OuterChild"));
        Assert.Equal("Host", outerChild.ContainerName);
        Assert.Equal("public partial class OuterChild { }", outerChild.Signature);
    }

    [Fact]
    public void Extract_CSharp_InlineBlockCommentBeforeSameLineClassDoesNotPolluteSignature()
    {
        // Inline block comments that end immediately before a real same-line declaration must
        // not leak into the emitted signature text. The real symbol should keep only the
        // canonical declaration slice. Closes #578.
        // same-line の実宣言直前で閉じる inline block comment は、出力 signature に
        // 混入してはならない。実在 symbol には正規の宣言部分だけを残す。Closes #578.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public partial class Host",
            "{",
            "    public partial class Wrapped<T>",
            "        where T : class",
            "    {",
            "        /* public partial class Child { } */ public partial class Child { }",
            "    }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var child = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Child"));
        Assert.Equal("Wrapped", child.ContainerName);
        Assert.Equal("public partial class Child { }", child.Signature);
    }

    [Fact]
    public void Extract_CSharp_InlineBlockCommentBeforeSameLinePropertyDoesNotPolluteSignature()
    {
        // Property signatures must also clamp from the declaration token after an inline
        // block comment, not from the comment prefix. Closes #578.
        // property の signature も inline block comment の先頭ではなく、その後ろの
        // 実宣言トークンから切り出さなければならない。Closes #578.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public class C",
            "{",
            "    /* public int P { get; } */ public int P { get; }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var property = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("C", property.ContainerName);
        Assert.Equal("public int P { get; }", property.Signature);
    }

    [Fact]
    public void Extract_CSharp_InlineBlockCommentBeforeSameLineMethodDoesNotPolluteSignature()
    {
        // Method signatures must ignore inline block-comment lookalikes that appear on the
        // same physical line before the real declaration. Closes #578.
        // method の signature も、同一物理行で実宣言より前にある inline block comment
        // 由来の見かけ上の宣言を無視しなければならない。Closes #578.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public class C",
            "{",
            "    /* public void M() { } */ public void M() { }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var method = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "M"));
        Assert.Equal("C", method.ContainerName);
        Assert.Equal("public void M() { }", method.Signature);
    }

    [Fact]
    public void Extract_CSharp_MultilineCommentCloseLineDoesNotEmitPhantomSameLineClass()
    {
        // A multiline block comment that closes immediately before a real same-line class
        // declaration must not leak comment-body lookalikes or `*/`-prefixed phantom rows
        // into symbol extraction. The current branch regression around wrapped same-line
        // occurrence recovery started emitting an extra `Child` row here. Closes #571.
        // 複数行 block comment の閉じ `*/` 直後に実在する same-line class 宣言が続く場合でも、
        // コメント本文の lookalike や `*/` 付き phantom 行を symbol 抽出へ漏らしてはならない。
        // wrapped same-line occurrence 修正の枝では、この fixture で余計な `Child` 行が
        // 追加で出る回帰が入っていた。Closes #571.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public partial class Host",
            "{",
            "    public partial class Wrapped<T>",
            "        where T : class",
            "    {",
            "        /*",
            "        public partial class Fake { } */ public partial class Child { } }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var children = symbols
            .Where(s => s.Kind == "class" && s.Name == "Child")
            .ToList();
        var child = Assert.Single(children);
        Assert.Equal("public partial class Child { }", child.Signature);
        Assert.Equal("class", child.ContainerKind);
        Assert.Equal("Wrapped", child.ContainerName);
        Assert.Equal(0, child.SameLineSignatureOccurrenceIndex);

        Assert.DoesNotContain(symbols, s =>
            s.Kind == "class"
            && s.Signature != null
            && s.Signature.Contains("Fake", StringComparison.Ordinal));
        Assert.DoesNotContain(symbols, s =>
            s.Kind == "class"
            && s.Signature != null
            && s.Signature.StartsWith("*/", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_CSharp_ClosingBraceLineKeepsInnerMemberAfterInnerMethodClosesSameLine()
    {
        // Wrapped closing-brace-line recovery must carry forward the unmatched brace depth
        // that is already open at the start of the end line. Otherwise the first `}` on the
        // line is mistaken for the wrapped type's close and later inner members fall out to
        // the outer type. Closes #575.
        // wrapped closing-brace-line の復元では、end line 開始時点ですでに開いている
        // unmatched brace depth を引き継がなければならない。そうしないと行頭側の
        // 最初の `}` を wrapped type 自身の閉じ括弧と誤認し、後続 inner member が
        // outer type 側へこぼれる。Closes #575.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public partial class Host",
            "{",
            "    public partial class Wrapped<T>",
            "        where T : class",
            "    {",
            "        public void M()",
            "        {",
            "        } public int P { get; } } public int Q { get; }",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var wrappedProperty = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "P"));
        Assert.Equal("class", wrappedProperty.ContainerKind);
        Assert.Equal("Wrapped", wrappedProperty.ContainerName);

        var hostProperty = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "Q"));
        Assert.Equal("class", hostProperty.ContainerKind);
        Assert.Equal("Host", hostProperty.ContainerName);
    }

    [Fact]
    public void Extract_CSharp_SameLineMultipleFieldsAreAllCaptured()
    {
        // `public class Multi { public int A; public int B; public int C; }` must
        // produce three `property` symbols (A, B, C) plus the outer `Multi` class, with
        // clean signatures that stop at the field terminator rather than trailing into
        // the enclosing `} }`. Closes #400.
        // `public class Multi { public int A; public int B; public int C; }` は外側 class
        // Multi と A / B / C の 3 つの property を生成し、signature は末尾の `} }` を
        // 含まずにフィールド終端（`;`）で切り詰められていなければならない。Closes #400.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public class Multi { public int A; public int B; public int C; }");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Multi");
        foreach (var name in new[] { "A", "B", "C" })
        {
            var field = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == name));
            Assert.Equal("class", field.ContainerKind);
            Assert.Equal("Multi", field.ContainerName);
            Assert.Equal($"public int {name};", field.Signature);
            Assert.DoesNotContain("}", field.Signature);
        }
    }

    [Fact]
    public void Extract_CSharp_SameLineNestedClassAttachesFieldToInnerType()
    {
        // `public class Outer { public class Inner { public int X; } }` must capture
        // both Outer and Inner, and the field X must attach to Inner (not Outer).
        // The container resolution uses a same-line `Signature.Contains` check, so the
        // plain-field signature clamp from #400 is required for Inner to correctly
        // "contain" X's signature. Closes #400.
        // `public class Outer { public class Inner { public int X; } }` では Outer と
        // Inner の双方を取得し、X は Outer ではなく Inner に紐づけること。同一行の
        // コンテナ解決は `Signature.Contains` を使うため、#400 で追加した plain-field
        // signature のクランプが無いと Inner が X の signature を「含む」と判定されず
        // X が Outer に吸収されてしまう。Closes #400.
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "",
            "public class Outer { public class Inner { public int X; } }");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Outer");
        var inner = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Inner"));
        Assert.Equal("class", inner.ContainerKind);
        Assert.Equal("Outer", inner.ContainerName);
        var x = Assert.Single(symbols.Where(s => s.Kind == "property" && s.Name == "X"));
        Assert.Equal("class", x.ContainerKind);
        Assert.Equal("Inner", x.ContainerName);
        Assert.Equal("public int X;", x.Signature);
    }

    [Fact]
    public void Extract_CSharp_SameLineCompactEnumMembersDoNotLeakAsFields()
    {
        // `public enum Mode { [Obsolete] A = (int)B, ... }` must produce enum-member
        // symbols only and must NOT emit phantom `property` symbols for `[Obsolete] A =`.
        // The column-aware scope gate distinguishes class-like bodies (where fields are
        // legal) from enum bodies (where members are not fields), so the plain-field
        // regex is rejected inside enum bodies. Closes #400.
        // `public enum Mode { [Obsolete] A = (int)B, ... }` は enum member のみを生成し、
        // `[Obsolete] A =` を property として拾ってはならない。列意識スコープゲートが
        // class-like body（field が正当）と enum body（member は field ではない）を
        // 区別するため、enum body 内では plain-field regex がリジェクトされる。
        // Closes #400.
        var content = string.Join(
            "\n",
            "using System;",
            "",
            "public enum Mode { [Obsolete] A = 1, B = 2, C = 3 }");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Mode");
        foreach (var name in new[] { "A", "B", "C" })
        {
            Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == name);
        }
    }

    [Fact]
    public void Extract_CSharp_SameLineFieldWithInitializerFollowedByAnotherField()
    {
        // `public class Holder { public int A = 1; public int B; }` must capture
        // both `A` and `B`. The prior fix broke out of the same-line scan as soon
        // as the plain-field pattern matched a `=`-terminated declaration, which
        // dropped any following same-line field statement. Closes #400.
        // `public class Holder { public int A = 1; public int B; }` では `A` と
        // `B` の両方が抽出される必要がある。旧修正は `=` 終端フィールドを拾った
        // 時点で同一行スキャンを break してしまい、直後の同一行フィールドを
        // 取り落としていた。Closes #400.
        var content = "public class Holder { public int A = 1; public int B; }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Holder");
        var a = Assert.Single(symbols, s => s.Kind == "property" && s.Name == "A");
        var b = Assert.Single(symbols, s => s.Kind == "property" && s.Name == "B");
        Assert.Equal("public int A = 1;", a.Signature);
        Assert.Equal("public int B;", b.Signature);
    }

    [Fact]
    public void Extract_CSharp_SameLineDeclaratorListFollowedByAnotherField()
    {
        // `public class Holder { public int A, B; public int C; }` must capture
        // three property rows (A, B via declarator list, plus C from the second
        // same-line field statement). The prior fix broke out of the same-line
        // scan after declarator expansion and silently dropped `C`. Closes #400.
        // `public class Holder { public int A, B; public int C; }` では declarator
        // list 展開で A と B、続く同一行 field 文から C、合わせて 3 シンボルを
        // 抽出する必要がある。旧修正は declarator 展開後に同一行スキャンを break
        // して C を取り落としていた。Closes #400.
        var content = "public class Holder { public int A, B; public int C; }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Holder");
        Assert.Single(symbols, s => s.Kind == "property" && s.Name == "A");
        Assert.Single(symbols, s => s.Kind == "property" && s.Name == "B");
        var c = Assert.Single(symbols, s => s.Kind == "property" && s.Name == "C");
        Assert.Equal("public int C;", c.Signature);
    }

    [Fact]
    public void Extract_CSharp_PlainFieldWithInitializerKeepsFullSignature()
    {
        // `private int _x = 42;` must store the full `private int _x = 42;` as
        // signature, not the `=`-truncated `private int _x =`. An earlier version
        // clamped the signature to `match.Length`, which cut off at `=`. The fix
        // clamps to the statement's `;` (or an unbalanced `}` if one is hit
        // first). Closes #400.
        // `private int _x = 42;` の signature は `=` で切り詰めず完全な
        // `private int _x = 42;` を保存する必要がある。旧実装は signature を
        // `match.Length` で clamp して `=` の手前で切れていた。修正では文終端の
        // `;` まで（あるいは先に出現する深さ 0 の `}` の手前まで）で clamp する。
        // Closes #400.
        var content = string.Join(
            "\n",
            "public class Holder",
            "{",
            "    private int _x = 42;",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var field = Assert.Single(symbols, s => s.Kind == "property" && s.Name == "_x");
        Assert.Equal("private int _x = 42;", field.Signature);
    }

    [Fact]
    public void Extract_CSharp_SameLineGenericClassHeaderStillCapturesInnerField()
    {
        // `public class C<T1, T2>{int X;}` must still capture field X even though
        // CollapseCSharpGenericTypeWhitespace removes the space inside `<T1, T2>`
        // when building the match line. Before the fix, the collapsed-space
        // `absoluteStartColumn` was handed directly to the raw-column
        // CSharpTypeBodyScope lookup, so the scope-gate fired too early and
        // the field was dropped. Closes #400.
        // `public class C<T1, T2>{int X;}` では CollapseCSharpGenericTypeWhitespace が
        // `<T1, T2>` の内部空白を詰めるため、collapsed 列 `absoluteStartColumn` を
        // そのまま raw 列ベースの CSharpTypeBodyScope に渡すと scope gate が誤発火し
        // X が落ちていた。Closes #400.
        var content = "public class C<T1, T2>{int X;}\n";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var field = Assert.Single(symbols, s => s.Kind == "property" && s.Name == "X");
        Assert.Equal("int X;", field.Signature);
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "C");
    }

    [Fact]
    public void Extract_CSharp_SameLineGenericFieldSignatureKeepsTerminator()
    {
        // `public Dictionary<string, int> Map = new(); public int B;` on one line
        // must produce Map with its trailing `;` and B without a leading `;`.
        // Before the fix, collapsed-space endpoints from
        // FindCSharpPlainFieldStatementEnd were used to slice the raw line, so
        // the generic-whitespace compression shifted the cut: Map lost its `;`
        // and B inherited the `;` as a leading character. Closes #400.
        // `public Dictionary<string, int> Map = new(); public int B;` を 1 行に
        // 書いた場合、Map の signature は末尾 `;` を保ち、B の signature は
        // 先頭 `;` を持たないこと。修正前は FindCSharpPlainFieldStatementEnd が
        // 返す collapsed 列で raw 行を slice していたため、generic 空白の圧縮
        // 分だけ切断位置がずれていた。Closes #400.
        var content = "public class C { public Dictionary<string, int> Map = new(); public int B; }\n";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var map = Assert.Single(symbols, s => s.Kind == "property" && s.Name == "Map");
        Assert.Equal("public Dictionary<string, int> Map = new();", map.Signature);

        var b = Assert.Single(symbols, s => s.Kind == "property" && s.Name == "B");
        Assert.Equal("public int B;", b.Signature);
    }

    [Fact]
    public void Extract_CSharp_MultiLineFieldFollowedBySameLineFieldDoesNotCrash()
    {
        // A multi-line field header whose continuation line also carries a
        // second same-line field — `public Dictionary<string, int>\n    Map = new(); public int B;`
        // — must extract both `Map` and `B` without throwing. In the prior fix,
        // the plain-field same-line scan continued past the first `;` using
        // absoluteStartColumn from the merged multi-line candidate, which sits
        // in the merged-string column domain and is not valid inside lines[i].
        // The follow-up regex hit then reached BuildCSharpMultilineSignature
        // with startColumn > lines[i].Length and crashed indexing with
        // `startIndex cannot be larger than length of string`. Closes #400.
        // 複数行 field ヘッダの continuation 行に 2 個目の同一行 field が続く
        // `public Dictionary<string, int>\n    Map = new(); public int B;` で、
        // 例外なく `Map` と `B` 両方が抽出できる必要がある。旧実装では plain-field の
        // 同一行継続 scan が、マージ済み候補の absoluteStartColumn を使って
        // statementEnd 以降へ進み、2 個目の regex マッチで
        // BuildCSharpMultilineSignature が lines[startLineIndex][startColumn..] で
        // 範囲外アクセスし `startIndex cannot be larger than length of string` で
        // indexing が落ちていた。Closes #400.
        var content = string.Join(
            "\n",
            "public class C",
            "{",
            "    public Dictionary<string, int>",
            "        Map = new(); public int B;",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "C");
        Assert.Single(symbols, s => s.Kind == "property" && s.Name == "Map");
        var b = Assert.Single(symbols, s => s.Kind == "property" && s.Name == "B");
        Assert.Equal("public int B;", b.Signature);
    }

    [Fact]
    public void Extract_CSharp_DetectsFunctionPointerField()
    {
        // Function-pointer field (`delegate*<int, void> Callback;`) must be captured.
        // The plain-field negative lookahead rejects `delegate` to stay away from
        // delegate-type declarations, but `delegate*` is a type form and the lookahead
        // uses `delegate\b(?!\*)` so it does not reject the function-pointer field.
        // Closes #298 follow-up (codex adversarial review).
        // function-pointer field（`delegate*<int, void> Callback;`）も抽出できること。
        // field pattern の negative lookahead は delegate 型宣言を除外するために
        // `delegate` を並べているが、`delegate*` は型なので `delegate\b(?!\*)` で
        // function-pointer field を排除しない。Closes #298 follow-up。
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "public unsafe class Bridge",
            "{",
            "    public delegate*<int, void> Callback;",
            "    private delegate* unmanaged[Cdecl]<int, int> _op;",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property"
            && s.Name == "Callback"
            && s.Visibility == "public");
        Assert.Contains(symbols, s => s.Kind == "property"
            && s.Name == "_op"
            && s.Visibility == "private");
    }

    [Fact]
    public void Extract_CSharp_DelegateTypeDeclarationIsNotField()
    {
        // `public delegate int Foo();` still declares a delegate type, not a field, so
        // the plain-field lookahead `delegate\b(?!\*)` must continue to reject it.
        // Closes #298 follow-up (codex adversarial review).
        // `public delegate int Foo();` は相変わらず delegate 型宣言であり field では
        // ないため、plain-field pattern の lookahead `delegate\b(?!\*)` がこれを
        // 引き続き排除することを確認する。Closes #298 follow-up。
        var content = string.Join(
            "\n",
            "namespace Demo;",
            "public class Host",
            "{",
            "    public delegate int Callback(int x);",
            "}");
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        // Accept either a dedicated delegate / function classification, but never
        // classify the statement as a plain `property` field.
        // delegate / function としての抽出は許容するが、`property` field にだけは
        // 分類しないことを確認する。
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "Callback");
    }

    [Fact]
    public void Extract_VB_DetectsSubFunctionClassModule()
    {
        // VB.NET: Sub, Function, Class, Module, Imports / VB.NET: サブ、関数、クラス、モジュール、Imports
        var content = "Imports System.IO\n\nPublic Class UserService\n    Public Sub Save(user As User)\n    End Sub\n\n    Private Function Validate(user As User) As Boolean\n        Return True\n    End Function\nEnd Class";
        var symbols = SymbolExtractor.Extract(1, "vb", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name.Contains("System.IO"));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Save");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Validate");
    }

    [Fact]
    public void Extract_VB_DetectsNamespaceAndImplicitVisibilityDeclarations()
    {
        var content = """
            Imports System

            Namespace MyApp
                Public Class Foo
                    Sub Bar()
                    End Sub

                    Function Quux() As Integer
                        Return 1
                    End Function
                End Class

                Class Helper
                End Class

                Public Module Utils
                    Sub Log()
                    End Sub
                End Module
            End Namespace
            """;
        var symbols = SymbolExtractor.Extract(1, "vb", content);

        var ns = Assert.Single(symbols.Where(s => s.Kind == "namespace" && s.Name == "MyApp"));
        Assert.Equal(3, ns.StartLine);
        Assert.Equal(20, ns.EndLine);

        var foo = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Foo"));
        Assert.Equal("namespace", foo.ContainerKind);
        Assert.Equal("MyApp", foo.ContainerName);

        var helper = Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Helper"));
        Assert.Equal("namespace", helper.ContainerKind);
        Assert.Equal("MyApp", helper.ContainerName);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Bar");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Quux");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Log");
    }

    [Fact]
    public void Extract_VB_DetectsLeadingModifiersAndMemberKindsWithoutVisibility()
    {
        var content = """
            Namespace MyApp
                Partial Class Form1
                End Class

                Public Class Widget
                    Partial Private Sub OnReady()
                    End Sub

                    Shared Sub Log()
                    End Sub

                    Overrides Function ToString() As String
                        Return ""
                    End Function

                    Property Count As Integer

                    Event Changed As EventHandler
                End Class
            End Namespace
            """;
        var symbols = SymbolExtractor.Extract(1, "vb", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Form1");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "OnReady");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Log");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ToString");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Count");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "Changed");
    }

    [Fact]
    public void Extract_Haskell_DetectsIndentedAndLiterateSignatures()
    {
        // Indented where-clause signature and literate Haskell '>' prefix
        // インデントされたwhere節のシグネチャとliterate Haskellの'>'接頭辞
        var content = "  helper :: Int -> Int\n  helper x = x + 1\n> main :: IO ()\n> main = putStrLn \"hello\"";
        var symbols = SymbolExtractor.Extract(1, "haskell", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "helper");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "main");
    }

    [Fact]
    public void Extract_R_DoesNotMatchOrdinaryAssignment()
    {
        // Ordinary assignment should not be detected as a function
        // 通常の代入は関数として検出されないこと
        var content = "x <- 42\ny <- some_func(x)\nz <- list(1, 2, 3)";
        var symbols = SymbolExtractor.Extract(1, "r", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "function");
    }

    [Fact]
    public void Extract_R_DetectsFunctionAssignmentAndLibrary()
    {
        // R: function assignment, library / R: 関数代入、library
        var content = "library(ggplot2)\n\nmy_plot <- function(data, x, y) {\n  ggplot(data, aes(x, y))\n}";
        var symbols = SymbolExtractor.Extract(1, "r", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "ggplot2");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "my_plot");
    }

    [Fact]
    public void Extract_Lua_DetectsFunctionsAndRequire()
    {
        // Lua: function, local function, assignment forms, require / Lua: 関数、ローカル関数、代入形式、require
        var content = """
            local http = require('socket.http')

            local helper = function(x)
              return x
            end

            M.named = function(name)
              return "hello " .. name
            end

            function M:method_form(arg)
              return arg
            end

            function M.dot_form(arg)
              return arg
            end

            local function top_local(x)
              return x
            end

            function plain_function(a)
              return a
            end
            """;
        var symbols = SymbolExtractor.Extract(1, "lua", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "socket.http");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "helper");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "M.named");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "M:method_form");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "M.dot_form");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "top_local");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "plain_function");
    }

    [Fact]
    public void Extract_Elixir_DetectsModuleAndFunctions()
    {
        // Elixir: defmodule, def, defp / Elixir: モジュール、関数、プライベート関数
        var content = "defmodule MyApp.Router do\n  def call(conn, _opts) do\n    conn\n  end\n\n  defp parse(data) do\n    data\n  end\nend";
        var symbols = SymbolExtractor.Extract(1, "elixir", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "MyApp.Router");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "call");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "parse");
    }

    [Fact]
    public void Extract_Elixir_NestedBlocks_AndDoShorthand_HaveMatchingBodyRanges()
    {
        // Elixir: nested fn/case/if/with bodies and `, do:` shorthand / ネストした fn/case/if/with と `, do:` 短縮形
        var content = "defmodule MyApp do\n  def process(items) do\n    Enum.each(items, fn item ->\n      IO.puts(\"item=#{item}\")\n    end)\n\n    Enum.map items, fn x -> x * 2 end\n    sigil = ~s(end)\n\n    case items do\n      [] -> :empty\n      [h | _] -> h\n    end\n\n    with {:ok, data} <- fetch(),\n         {:ok, parsed} <- parse(data) do\n      IO.puts(parsed)\n    end\n  end\n\n  def fetch do\n    if true do\n      {:ok, \"data\"}\n    else\n      :error\n    end\n  end\n\n  def parse(data), do: {:ok, data}\n\n  def quick_check, do: helper()\n\n  def helper, do: :ok\nend";
        var symbols = SymbolExtractor.Extract(1, "elixir", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "MyApp" && s.StartLine == 1 && s.EndLine == 34);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "process" && s.StartLine == 2 && s.EndLine == 19 && s.BodyStartLine == 3 && s.BodyEndLine == 19);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "fetch" && s.StartLine == 21 && s.EndLine == 27 && s.BodyStartLine == 22 && s.BodyEndLine == 27);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "parse" && s.StartLine == 29 && s.EndLine == 29 && s.BodyStartLine == 29 && s.BodyEndLine == 29);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "quick_check" && s.StartLine == 31 && s.EndLine == 31 && s.BodyStartLine == 31 && s.BodyEndLine == 31);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "helper" && s.StartLine == 33 && s.EndLine == 33 && s.BodyStartLine == 33 && s.BodyEndLine == 33);
    }

    [Fact]
    public void Extract_Scala_DetectsObjectTraitAndDef()
    {
        // Scala: object, trait, def, case class / Scala: オブジェクト、トレイト、def、ケースクラス
        var content = """
            object Main {
              def one() = 1
              def run(): Unit = {
                2
              }
              def three() = 3
              type Callback = Int => Int
              type UserID = Int
            }
            sealed trait Message
            case class Ping(id: Int) extends Message
            """;
        var symbols = SymbolExtractor.Extract(1, "scala", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Main");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "one" && s.StartLine == 2 && s.EndLine == 2 && s.BodyStartLine == null && s.BodyEndLine == null);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.StartLine == 3 && s.EndLine == 5 && s.BodyStartLine == 3 && s.BodyEndLine == 5);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "three" && s.StartLine == 6 && s.EndLine == 6 && s.BodyStartLine == null && s.BodyEndLine == null);
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Callback");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "UserID");
        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "Message");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Ping");
    }

    [Fact]
    public void Extract_Dart_DetectsClassFunctionAndMixin()
    {
        // Dart: class, mixin, function / Dart: クラス、mixin、関数
        var content = "abstract class Widget {\n  void build(BuildContext ctx) {\n  }\n}\nmixin Logging on Widget {\n}";
        var symbols = SymbolExtractor.Extract(1, "dart", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Widget");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "build");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Logging");
    }

    [Fact]
    public void Extract_Dart_DetectsConstructorsAndTypedefAliases()
    {
        // Dart: constructors, typedef aliases / Dart: コンストラクタ、typedef エイリアス
        var content = """
            abstract class Animal {
              String name;
              int age;

              Animal(this.name, this.age);
              Animal.named({required this.name, this.age = 0});
              factory Animal.empty() => _EmptyAnimal();
              factory Animal.fromJson(Map<String, dynamic> json) =>
                  Animal(json['name'] as String, json['age'] as int);
              const Animal.constant(this.name, this.age);
            }

            class Point {
              const Point(this.x, this.y);

              final int x;
              final int y;
            }

            class Token {
              const Token();
              const Token.named();

              void demo() {
                const Widget(key: k);
              }
            }

            class _EmptyAnimal extends Animal {
              _EmptyAnimal() : super('', 0);
            }

            typedef IntCallback = int Function(int value);
            typedef StringMap = Map<String, String>;
            typedef int LegacyCallback(int value);
            """;

        var symbols = SymbolExtractor.Extract(1, "dart", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Animal");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Animal.named");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Animal.empty");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Animal.fromJson");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Animal.constant");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Point");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Token");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Token.named");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "_EmptyAnimal");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "IntCallback");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "StringMap");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "LegacyCallback");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Widget");
    }

    [Fact]
    public void Extract_Dart_DoesNotTreatKeywordLookalikesAsFunctions()
    {
        var content = """
            abstract class Widget {}

            String bar() => 'bar';

            void sample(bool first, bool secondReady) {
              if (first) {
              }
              else if (secondReady) {
              }
            }

            void handle(Object value) {
              switch (value) {
                case const Class():
                  break;
              }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "dart", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Widget");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "bar");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "sample");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "handle");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "if");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Class");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "class");
    }

    [Fact]
    public void Extract_Dart_DetectsImportAndEnum()
    {
        // Dart: import, enum / Dart: インポート、列挙型
        var content = "import 'package:flutter/material.dart';\n\nenum Status {\n  active,\n  inactive,\n}";
        var symbols = SymbolExtractor.Extract(1, "dart", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name.Contains("flutter"));
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Status");
    }

    [Fact]
    public void Extract_Dart_DoesNotMatchExpressionLines()
    {
        // Expressions that look like "type name(" but are not function definitions
        // 関数定義に見えるが実際は式の行を誤検出しないことを検証
        var content = "void main() {\n  return foo(bar);\n  await task(x);\n  const Widget(key: k);\n  throw Error('oops');\n}";
        var symbols = SymbolExtractor.Extract(1, "dart", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "foo");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "task");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Widget");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Error");
        // main() should still be detected / main()は検出されるべき
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "main");
    }

    [Fact]
    public void Extract_GraphQL_DetectsSymbols()
    {
        var content = """
            type User {
              id: ID!
              name: String!
            }

            input CreateUserInput {
              name: String!
              email: String!
            }

            enum Role {
              ADMIN
              USER
            }

            query GetUser($id: ID!) {
              user(id: $id) { name }
            }

            mutation CreateUser($input: CreateUserInput!) {
              createUser(input: $input) { id }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "graphql", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "CreateUserInput");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Role");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetUser");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "CreateUser");
    }

    [Fact]
    public void Extract_GraphQL_DetectsFragmentsDirectivesAndExtends()
    {
        var content = """
            type User {
              id: ID!
            }

            input CreateUserInput {
              name: String!
            }

            interface Node {
              id: ID!
            }

            enum Role {
              ADMIN
              USER
            }

            fragment ProcessingTimeoutError on ProcessingTimeoutError {
              __typename
              message
            }

            directive @auth(role: String!) on FIELD_DEFINITION

            extend type ExtendedUser {
              profile: Profile
            }

            extend interface ExtendedNode {
              archived: Boolean
            }

            extend input ExtendedCreateUserInput {
              email: String
            }

            extend enum ExtendedRole {
              GUEST
            }

            extend union SearchResult = User | Organization

            extend scalar DateTime

            query GetUser($id: ID!) {
              user(id: $id) { name }
            }

            mutation CreateUser($input: CreateUserInput!) {
              createUser(input: $input) { id }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "graphql", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "CreateUserInput");
        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "Node");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Role");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ProcessingTimeoutError");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "auth");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "ExtendedUser");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "ExtendedNode");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "ExtendedCreateUserInput");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "ExtendedRole");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "SearchResult");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "DateTime");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetUser");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "CreateUser");
    }

    [Fact]
    public void Extract_Gradle_DetectsSymbols()
    {
        // Both legacy apply plugin: and modern plugins { id '...' } forms
        // レガシーの apply plugin: と新しい plugins { id '...' } の両形式
        var content = "apply plugin: 'java'\n\nplugins {\n  id 'org.springframework.boot'\n}\n\ntask build {\n  doLast { println 'Building' }\n}\n\ndef customTask {\n  println 'custom'\n}\n";
        var symbols = SymbolExtractor.Extract(1, "gradle", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "java");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "org.springframework.boot");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "build");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "customTask");
    }

    [Fact]
    public void Extract_CSharp_DoesNotMatchCallSitesAsDefinitions()
    {
        // Issue #40: await/return/throw calls should not be extracted as method definitions
        // Issue #40: await/return/throw の呼び出しがメソッド定義として抽出されないこと
        var content = "public class Service\n{\n    public async Task InitAsync()\n    {\n        await TryNotifyAsync(true);\n        return GetResult();\n        throw CreateException(\"err\");\n        var x = ComputeValue(42);\n        yield return GenerateItem();\n    }\n\n    private async Task TryNotifyAsync(bool force)\n    {\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        // Real definitions should be extracted / 実際の定義は抽出されるべき
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "InitAsync");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "TryNotifyAsync");
        // Call sites should NOT be extracted as definitions / 呼び出し箇所は定義として抽出されないこと
        Assert.DoesNotContain(symbols, s => s.Name == "TryNotifyAsync" && s.Line == 5);
        Assert.DoesNotContain(symbols, s => s.Name == "GetResult");
        Assert.DoesNotContain(symbols, s => s.Name == "CreateException");
        Assert.DoesNotContain(symbols, s => s.Name == "ComputeValue");
        Assert.DoesNotContain(symbols, s => s.Name == "GenerateItem");
    }

    [Fact]
    public void Extract_CSharp_DoesNotMatchQualifiedCallSitesAsDefinitions()
    {
        // Qualified call sites (obj.Method()) should not match the explicit interface pattern
        // 修飾付き呼び出し (obj.Method()) は明示的インターフェース実装パターンにマッチしないこと
        var content = "public class Service\n{\n    public async Task Run()\n    {\n        return service.GetResult();\n        await client.SendAsync();\n        throw factory.CreateException(\"err\");\n    }\n\n    void IDisposable.Dispose()\n    {\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        // Explicit interface impl should be extracted / 明示的インターフェース実装は抽出されるべき
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Dispose" && s.ReturnType == "void");
        // Qualified call sites should NOT be extracted / 修飾付き呼び出しは抽出されないこと
        Assert.DoesNotContain(symbols, s => s.Name == "GetResult");
        Assert.DoesNotContain(symbols, s => s.Name == "SendAsync");
        Assert.DoesNotContain(symbols, s => s.Name == "CreateException");
    }

    [Fact]
    public void Extract_CSharp_DoesNotMatchNewExpressionStatementsAsExplicitInterfaceDefinitions()
    {
        // Issue #362: `new System.Text.StringBuilder().Append(...)` などの式文が、
        // 正規表現が最初の `(` で止まるために returnType=`new` / interface=手前の修飾チェーン
        // （namespace `System.Text` / 外側型 `Outer` / `MyApp.Outer` のような両者の混在など、
        // ドット連鎖そのもの。この位置では namespace と外側型を区別しない）/
        // name=構築されている型（`StringBuilder` / `HttpClient` / `Inner`）として
        // 明示的インターフェースメソッド定義に化けないこと。ブレース初期化子形
        // (`new Outer.Inner { A = 1 }.Consume();`) と `_ = new ...` 形も同じフィクスチャで
        // 固定し、brace-initializer 側では `Outer.Inner` / `Inner.Consume` のコンテナ関係
        // と `Consume` が全体 1 本だけ（= brace-init から phantom `function Consume` が
        // 増えない、別コンテナ配下や container 未設定の phantom も出ない）ことまで
        // ピン留めする。
        // Issue #362: expression statements like `new System.Text.StringBuilder().Append(...)`
        // must not masquerade as explicit interface method definitions. The phantom name would
        // be the identifier right before the first `(` — the type being constructed
        // (`StringBuilder` / `HttpClient` / `Inner`), because the explicit-interface regex
        // stops at the first `(` and consumes the preceding dot-chain as the would-be
        // interface qualifier. That qualifier may be a namespace prefix (`System.Text` in
        // `new System.Text.StringBuilder()`), an enclosing-type chain (`Outer` in
        // `new Outer.Inner()` where `Outer` is an outer class, not a namespace), or a
        // mix of both (e.g. `new MyApp.Outer.Inner()` where `MyApp` is a namespace and
        // `Outer` is an enclosing type) — the regex does not distinguish which segments are
        // namespaces and which are enclosing types at this position. Brace-initializer forms
        // (`new Outer.Inner { A = 1 }.Consume();`) and discard forms (`_ = new ...`) are
        // also pinned here; the brace-initializer case additionally pins the real
        // `Outer` → `Inner` → `Consume` container chain and that exactly one `Consume`
        // row is emitted (no phantom `function Consume` leaking out of the brace-init site).
        var content = "public class Svc\n{\n    public int Real() => 42;\n\n    public void ChainedNew()\n    {\n        new System.Text.StringBuilder().Append(\"a\").Append(\"b\").ToString();\n    }\n\n    public void DiscardNew()\n    {\n        _ = new System.Text.RegularExpressions.Regex(\"pattern\");\n    }\n\n    public void UseNew()\n    {\n        new System.Net.Http.HttpClient().Dispose();\n    }\n\n    public void BraceInitNew()\n    {\n        new Outer.Inner { A = 1 }.Consume();\n    }\n}\n\npublic class Outer\n{\n    public class Inner { public int A { get; set; } public void Consume() { } }\n}\n\npublic class Consumer : System.IDisposable\n{\n    void System.IDisposable.Dispose() { }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        // Real definitions should be extracted / 実際の定義は抽出されるべき
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Svc");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Real");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ChainedNew");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "DiscardNew");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "UseNew");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "BraceInitNew");
        // Nested Outer.Inner container chain must be preserved, and exactly one Consume symbol
        // must exist in total — emitted under Inner. Two guards here:
        //   (a) the total `Consume` count across ALL kinds / containers must be 1, so a
        //       phantom `function Consume` emitted under `Svc` (or with no container at all)
        //       from `new Outer.Inner { A = 1 }.Consume();` cannot sneak past by living
        //       outside `ContainerName == "Inner"`; and
        //   (b) that single `Consume` must be a `function` under `Inner` under `Outer`, so a
        //       broken container chain also fails.
        // ネストした Outer.Inner のコンテナ関係を固定。`Consume` は全体 1 本のみ（別コンテナ
        // 配下や container 未設定の phantom も弾く）かつ、その 1 本は `Inner` 配下に属する
        // `function` である、という二段のガードで brace-init phantom を検出する。
        Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Outer"));
        Assert.Single(symbols.Where(s => s.Name == "Inner"));
        Assert.Single(symbols.Where(s => s.Kind == "class" && s.Name == "Inner" && s.ContainerKind == "class" && s.ContainerName == "Outer"));
        Assert.Single(symbols.Where(s => s.Name == "Consume"));
        Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Consume" && s.ContainerKind == "class" && s.ContainerName == "Inner"));
        // Explicit interface impl on Consumer class must still be captured (regression guard)
        // Consumer クラスの明示的インターフェース実装は引き続き抽出されること（回帰防止）
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Dispose" && s.ReturnType == "void");
        // Phantom function rows from new-expression statements must NOT be produced,
        // whether the chain ends in parentheses (`new T().M(...)`) or a brace-initializer
        // (`new T { ... }.M(...)`). 構築される型名 (`StringBuilder` / `HttpClient` / `Regex`
        // / `Inner`) が function 行として出ないこと。
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "StringBuilder");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Regex");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "HttpClient");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Inner" && s.ReturnType == "new");
        // Kind-agnostic guard specifically against this #362 phantom shape: the `new` keyword
        // itself must not sneak in under ANY kind inside this fixture. This is a targeted
        // guard, not a general "the word `new` can never be a symbol name anywhere" claim.
        // Issue #362 の phantom が将来別 kind に分類し直されても取りこぼさないための
        // kind 非依存ガード。ここでの意味は「このフィクスチャの範囲内で `new` が名前に
        // 出てこない」ことに限定しており、一般命題として主張するものではない。
        Assert.DoesNotContain(symbols, s => s.Name == "new");
    }

    [Fact]
    public void Extract_CSharp_DoesNotMatchQualifiedNewExpressionsAsExplicitInterfaceDefinitions()
    {
        // Issue #362: qualified constructor expressions (`new Namespace.Type()`) must not be
        // misread as `returnType + interface.member` by the explicit-interface regex.
        // Issue #362: 修飾付きコンストラクタ式 (`new Namespace.Type()`) を、明示的インターフェース
        // 実装 regex の `returnType + interface.member` と誤認しないこと。
        var content = """
            public class Service : IDisposable
            {
                public void Build()
                {
                    new System.Text.StringBuilder().Append("a").Append("b").ToString();
                    _ = new System.Text.RegularExpressions.Regex("pattern");
                    new System.Net.Http.HttpClient().Dispose();
                }

                void IDisposable.Dispose()
                {
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Build" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Dispose" && s.ReturnType == "void");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "StringBuilder");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Regex");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "HttpClient");
    }

    [Fact]
    public void Extract_CSharp_DoesNotMatchNamedArgumentFrameworkCallsAsDefinitions()
    {
        // Named-argument labels preceding qualified framework calls must not look like explicit interface impls.
        // 修飾付き framework call の前にある named-argument label を明示的インターフェース実装と誤認しないこと。
        var content = """
            public class PlatformState
            {
                public PlatformState(bool isWindows, bool isMacCatalyst)
                {
                }

                public static PlatformState Detect() =>
                    new(
                        isWindows: OperatingSystem.IsWindows(),
                        isMacCatalyst: OperatingSystem.IsMacCatalyst());
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "PlatformState");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Detect");
        Assert.DoesNotContain(symbols, s => s.Name == "IsWindows");
        Assert.DoesNotContain(symbols, s => s.Name == "IsMacCatalyst");
    }

    [Fact]
    public void Extract_CSharp_DetectsAliasQualifiedExplicitInterfaceImplementations()
    {
        // Alias-qualified return types should still match explicit interface implementations.
        // alias-qualified な戻り値型でも明示的インターフェース実装として抽出されること。
        var content = """
            public interface IFoo
            {
                string Name();
                object Create();
            }

            public class Impl : IFoo
            {
                global::System.String IFoo.Name() => "x";
                Alias::Type IFoo.Create() => default;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Name" && s.ReturnType == "global::System.String");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Create" && s.ReturnType == "Alias::Type");
    }

    [Fact]
    public void Extract_CSharp_DoesNotMatchTernaryContinuationCallsAsDefinitions()
    {
        var content = """
            public class Dispatcher
            {
                private string Select(bool isUpdate)
                    => isUpdate
                        ? RunUpdateMode()
                        : RunFullScan();

                private string RunUpdateMode() => "update";
                private string RunFullScan() => "full";

                public int? NullableCount() => null;

                public Dispatcher(bool isUpdate)
                    : base()
                {
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Select");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "RunUpdateMode" && s.Line == 8);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "RunFullScan" && s.Line == 9);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "NullableCount" && s.ReturnType == "int?");
        Assert.DoesNotContain(symbols, s => s.Name == "RunUpdateMode" && s.Line == 5);
        Assert.DoesNotContain(symbols, s => s.Name == "RunFullScan" && s.Line == 6);
        Assert.DoesNotContain(symbols, s => s.Name == "base");
    }

    [Fact]
    public void Extract_CSharp_DetectsNewModifierMethods()
    {
        // C# `new` modifier for member hiding should still be extracted as definitions
        // C# のメンバー隠蔽用 `new` 修飾子は定義として抽出されるべき
        var content = "public class Derived : Base\n{\n    new void Reset() { }\n    new int Compare(object obj) { return 0; }\n    public new string ToString() { return \"\"; }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Reset");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Compare");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ToString");
    }

    [Fact]
    public void Extract_Python_DetectsPropertyDecorator()
    {
        var content = "class User:\n    @property\n    def name(self):\n        return self._name\n\n    def greet(self):\n        print(self.name)";
        var symbols = SymbolExtractor.Extract(1, "python", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "name");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "greet");
    }

    [Theory]
    [InlineData("csharp", "public interface IFoo { }", "interface")]
    [InlineData("csharp", "public enum Color { }", "enum")]
    [InlineData("csharp", "public struct Point { }", "struct")]
    [InlineData("csharp", "public delegate void Handler();", "delegate")]
    [InlineData("csharp", "public event EventHandler Click;", "event")]
    [InlineData("csharp", "public string Name { get; set; }", "property")]
    [InlineData("java", "interface Foo { }", "interface")]
    [InlineData("java", "enum Color { RED }", "enum")]
    [InlineData("kotlin", "interface Foo", "interface")]
    [InlineData("kotlin", "enum class Color { RED }", "enum")]
    [InlineData("kotlin", "val name: String = \"\"", "property")]
    [InlineData("typescript", "export interface IFoo { }", "interface")]
    [InlineData("typescript", "export enum Status { A }", "enum")]
    [InlineData("go", "type Foo struct { }", "struct")]
    [InlineData("go", "type Foo interface { }", "interface")]
    [InlineData("rust", "pub struct Config { }", "struct")]
    [InlineData("rust", "pub enum Color { }", "enum")]
    [InlineData("rust", "pub trait Foo { }", "interface")]
    [InlineData("swift", "struct Config { }", "struct")]
    [InlineData("swift", "enum Color { }", "enum")]
    [InlineData("swift", "protocol Foo { }", "interface")]
    [InlineData("c", "struct Config { };", "struct")]
    [InlineData("c", "enum Color { RED };", "enum")]
    [InlineData("cpp", "struct Config { };", "struct")]
    [InlineData("cpp", "enum Color { RED };", "enum")]
    [InlineData("php", "interface Foo { }", "interface")]
    [InlineData("php", "enum Color { }", "enum")]
    [InlineData("scala", "trait Foo", "interface")]
    [InlineData("scala", "enum Color", "enum")]
    [InlineData("dart", "enum Status { active }", "enum")]
    [InlineData("graphql", "interface Node { }", "interface")]
    [InlineData("graphql", "enum Role { ADMIN }", "enum")]
    [InlineData("haskell", "class Functor f where", "interface")]
    [InlineData("elixir", "defprotocol Enumerable do\nend", "interface")]
    [InlineData("ruby", "  attr_accessor :name", "property")]
    [InlineData("python", "@property\ndef name(self):", "property")]
    public void Extract_CrossLanguage_GranularKindsAreConsistent(string lang, string content, string expectedKind)
    {
        var symbols = SymbolExtractor.Extract(1, lang, content);
        Assert.Contains(symbols, s => s.Kind == expectedKind);
    }

    [Fact]
    public void EstimateComplexity_StraightLineFunction_ReturnsOne()
    {
        var body = "var x = 1;\nreturn x + 2;";
        Assert.Equal(1, SymbolExtractor.EstimateComplexity(body));
    }

    [Fact]
    public void EstimateComplexity_BranchingFunction_CountsBranches()
    {
        var body = "if (x > 0)\n    return x;\nelse if (x < 0)\n    return -x;\nfor (int i = 0; i < n; i++)\n    sum += i;\nwhile (retry)\n    attempt();\nvar result = a ?? b;\nvar flag = x && y || z;";
        // 1 (baseline) + if + else if + for + while + ?? + && + || = 8
        Assert.Equal(8, SymbolExtractor.EstimateComplexity(body));
    }

    [Fact]
    public void EstimateComplexity_EmptyBody_ReturnsOne()
    {
        Assert.Equal(1, SymbolExtractor.EstimateComplexity(""));
        Assert.Equal(1, SymbolExtractor.EstimateComplexity("   "));
    }

    [Fact]
    public void Extract_CSS_DetectsSymbols()
    {
        var content = """
            @import 'reset.css';
            @forward 'theme';

            $primary-color: #333;

            @function shade-color($color, $weight) {
              @return $color;
            }

            @mixin flex-center {
              display: flex;
              align-items: center;
            }

            @keyframes fade-in {
              from { opacity: 0; }
              to { opacity: 1; }
            }

            .container {
              max-width: 1200px;
            }

            #header {
              background: $primary-color;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "css", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name.Contains("reset.css"));
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name.Contains("theme"));
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "primary-color");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "shade-color");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "flex-center");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "fade-in");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ".container");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "#header");
    }

    [Fact]
    public void Extract_CSS_DetectsCustomPropertiesFontFacesAndSelectorVariants()
    {
        var content = """
            :root {
              --accent-color: #09f;
            }

            @font-face {
              font-family: "Block Font";
              src: url("block.woff2");
            }

            @FONT-FACE {
              font-family:
                "Split Font";
              src: url("split.woff2");
            }

            a:hover {
              color: red;
            }

            .btn.primary {
              color: green;
            }

            .alert .link {
              color: orange;
            }

            input[type="text"] {
              color: blue;
            }

            [hidden] {
              display: none;
            }

            .btn::before {
              content: "";
            }

            %button-base {
              padding: 4px;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "css", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ":root");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "--accent-color");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Block Font");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Split Font");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "a:hover");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ".btn");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ".alert");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "input[type=\"text\"]");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "[hidden]");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ".btn::before");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "%button-base");
    }

    [Fact]
    public void Extract_CSS_DetectsInlineFontFaceFamilyNames()
    {
        var content = """
            @font-face { font-family: "Inline Font"; src: url("inline.woff2"); }
            @font-face { src: url("same-line.woff2"); font-family: "Trailing Font"; unicode-range: U+0-5FF; }
            @font-face { src: url("valid-last.woff2"); font-family: "Last No Semicolon" }
            @font-face { font-family: /* keep */ "Comment Gap"; src: url("comment-gap.woff2"); }
            @font-face {
              src: url("commented.woff2");
              /* font-family: bogus; */
              font-family: "Commented Font";
            }
            @font-face {
              src: url("data:application/font-woff2;charset=utf-8;base64,font-family:bogus");
              font-family: "Real Font";
            }
            @font-face { src: url(data:text/plain;charset=utf-8;foo=1;font-family:bogus); font-family: Real Data Font; }
            @font-face { src: url(data:image/svg+xml,<svg>{}</svg>); font-family: Svg Data Font; }
            @font-face { src: url("no-family.woff2"); }
            @font-face {
              font-family:
                "Split Font";
              src: url("split.woff2");
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "css", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Inline Font");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Trailing Font");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Last No Semicolon");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Comment Gap");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Commented Font");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Real Font");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Real Data Font");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Svg Data Font");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Split Font");
        Assert.DoesNotContain(symbols, s => s.Name == "@font-face");
        Assert.DoesNotContain(symbols, s => s.Name == "bogus)");
    }

    [Fact]
    public void Extract_CSS_CapturesCommaSeparatedSelectorListsAndNamedAtRules()
    {
        var content = """
            .btn, .link { color: red; }
            #nav, #header { display: flex; }

            @counter-style circled {
              system: fixed;
              symbols: \2460 \2461;
            }

            @layer reset, base, theme;
            @namespace svg url("http://www.w3.org/2000/svg");

            @page :first {
              margin: 2cm;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "css", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ".btn");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ".link");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "#nav");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "#header");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "circled");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "reset");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "base");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "theme");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "svg");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == ":first");
    }

    [Fact]
    public void Extract_CSS_CapturesGroupingAtRulesAndNativeNestingSelectors()
    {
        var content = """
            @layer components {
              .layer-class {
                &:hover {
                  color: blue;
                }

                & .icon {
                  color: white;
                }

                &.modifier {
                  color: green;
                }

                & > .child {
                  color: yellow;
                }
              }
            }

            @container (min-width: 500px) {
              .container-class {
                display: flex;
              }
            }

            @supports (display: grid) {
              .supports-class {
                display: grid;
              }
            }

            @media screen {
              .media-class {
                color: red;
              }
            }

            .parent {
              .nested-child {
                color: blue;
              }
            }

            @media screen { .inline-media { color: red; } }
        """;
        var symbols = SymbolExtractor.Extract(1, "css", content);

        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "layer");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "container");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "supports");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "media");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ".layer-class");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ".container-class");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ".supports-class");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ".media-class");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ".inline-media");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "hover");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "icon");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "modifier");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "child");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == ".nested-child");
    }

    [Fact]
    public void Extract_CSS_DoesNotLeakNestedSelectorsAfterSameLineGroupingAndQualifiedRule()
    {
        var content = """
            @media screen { .outer {
              .inner { color: red; }
            } }
            """;
        var symbols = SymbolExtractor.Extract(1, "css", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == ".inner");
    }

    [Fact]
    public void Extract_CSS_TopLevelDetection_IgnoresScssLineCommentsAndEscapedQuotes()
    {
        var content = """
            // {
            .top-level { color: red; }

            .foo::before { content: "\""; }
            .bar { color: blue; }

            .parent { background-image: url(http://example.com/a.png); }
            .top-level:hover { color: green; }

            .parent2 { background-image: url(//cdn.example.com/app.css); }
            [data-theme="dark"] { color: white; }
            """;
        var symbols = SymbolExtractor.Extract(1, "css", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ".top-level");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ".foo::before");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ".bar");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ".top-level:hover");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "[data-theme=\"dark\"]");
    }

    [Fact]
    public void Extract_CSS_PreservesLiteralSelectorNames()
    {
        var content = """
            :root { --accent: #09f; }
            .root { color: red; }
            #root { color: blue; }
            """;
        var symbols = SymbolExtractor.Extract(1, "css", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ":root");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "--accent");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == ".root");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "#root");
    }

    [Fact]
    public void Extract_PowerShell_DetectsSymbols()
    {
        var content = """
            Import-Module ActiveDirectory
            using module PSDesiredStateConfiguration
            using namespace System.IO
            using assembly System.Xml.Linq

            configuration MyConfig {
                Node 'localhost' { }
            }

            workflow TestFlow {
                Get-Process
            }

            class ServerConfig {
                [string]$Name
            }

            enum Environment {
                Dev
                Staging
                Prod
            }

            function Get-UserInfo {
                param($UserId)
                Get-ADUser -Identity $UserId
            }

            function script:Private-Helper { return 42 }
            function global:Setup-Env {
                $env:APP_MODE = 'dev'
            }
            function local:Inner-Helper { return 'inner' }
            function private:InternalUtil { return 'util' }

            filter script:Where-Active {
                if ($_.Enabled) { $_ }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "powershell", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "ActiveDirectory");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "PSDesiredStateConfiguration");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "System.IO");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "System.Xml.Linq");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MyConfig");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "TestFlow");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "ServerConfig");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Environment");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Get-UserInfo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Private-Helper");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Setup-Env");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Inner-Helper");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "InternalUtil");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Where-Active");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "script");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "global");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "local");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "private");
    }

    [Fact]
    public void Extract_PowerShell_DetectsClassMembersAndEnumValues()
    {
        var content = """
            class Vehicle {
                [DscProperty(Key)] [string]$Name
                hidden [string]$Secret

                Vehicle([string]$make) {
                    $this.Name = $make
                }

                [string] ToString() {
                    return "$($this.Name)"
                }

                static [Vehicle] CreateDefault() {
                    return [Vehicle]::new("Unknown")
                }

                hidden [void] InternalMethod() {
                }
            }

            enum LogLevel {
                Debug
                Info = 1
                Warning
                Error
            }

            class MyDscResource {
                [DscProperty(Key)] [string]$Name

                [MyDscResource] Get() {
                    return $this
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "powershell", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Vehicle");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Name");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Secret");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Vehicle");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ToString");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "CreateDefault");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "InternalMethod");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "LogLevel");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Debug");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Info");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Warning");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Error");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "MyDscResource");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Name");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Get");
    }

    [Fact]
    public void Extract_Batch_DetectsLabelsAndSetAssignments()
    {
        // Covers issue #217: batch (.bat / .cmd) labels are the only navigation anchors
        // in a batch script (goto :X / call :X targets). Without label symbols every batch
        // file indexed with zero symbol rows. Also pins:
        //   - `:EOF` is the reserved `goto :EOF` / `call :EOF` target and must NOT surface.
        //   - `::` / `:::` comment lines must not produce bogus symbols.
        //   - `SET` / `Set` / `SET /A` / `SET /P` variations are all picked up.
        //   - CRLF line endings behave the same as LF.
        //   - Echo-suppression `@set VAR=...` (with or without whitespace after `@`).
        //   - `set /a VAR+=1` and other compound arithmetic operators (`-=`, `*=`, `/=`,
        //     `%=`, `&=`, `|=`, `^=`, `<<=`, `>>=`).
        //   - `if <cond> set VAR=...` style one-line conditional assignments.
        //   - Same-line multi-statement forms emit one symbol per `set`: `&`-chained
        //     (`set A=1 & set B=2`), `if ... & set` (`if exist x set C=3 & set D=4`),
        //     parenthesized + `else` (`if exist x ( set E=5 ) else set F=6`), and
        //     `for ... do set` (`for %%I in (1) do set LOOPVAR=%%I`).
        //   - `rem` / `@rem` / `::` comment lines do NOT emit phantom `set` symbols even when
        //     the comment body contains the new boundary tokens (`&`, `(`, `else`, `do`).
        //   - Dotted labels such as `:build.release` are captured in full, not truncated.
        // issue #217 対応: batch (.bat / .cmd) のラベルは batch スクリプトにおける唯一の
        // ナビゲーションアンカー (goto :X / call :X の着地点)。ラベルシンボルが無いと
        // 全ての batch ファイルがシンボル 0 件のまま索引されてしまっていた。あわせて以下を固定:
        //   - `:EOF` は `goto :EOF` / `call :EOF` 用の予約ターゲットなのでシンボル化しない。
        //   - `::` / `:::` コメント行は偽シンボルを生成しない。
        //   - `SET` / `Set` / `SET /A` / `SET /P` の大小文字混在・オプション違いも拾う。
        //   - CRLF 行末でも LF と同じ結果になる。
        //   - echo 抑止プレフィクス付きの `@set VAR=...` (`@` 直後の空白有無を含む) を拾う。
        //   - `set /a VAR+=1` および `-=` / `*=` / `/=` / `%=` / `&=` / `|=` / `^=` / `<<=` / `>>=`
        //     の複合演算子も拾う。
        //   - `if <cond> set VAR=...` 形式の 1 行条件付き代入も拾う。
        //   - 同一行複数ステートメント形を 1 `set` ごとに 1 シンボルとして拾う:
        //     `&` 連結 (`set A=1 & set B=2`) 、`if ... & set` (`if exist x set C=3 & set D=4`) 、
        //     括弧 + `else` (`if exist x ( set E=5 ) else set F=6`) 、
        //     `for ... do set` (`for %%I in (1) do set LOOPVAR=%%I`)。
        //   - `rem` / `@rem` / `::` コメント行は、本文に新しい境界トークン (`&` / `(` / `else` / `do`) を
        //     含んでいても偽の `set` シンボルを出さない。
        //   - `:build.release` のようなドット付きラベルは切り詰めずフル名で取得する。
        // Fixture also includes `:eof2` / `:eofish` / `:end-of-file` so the `(?!eof(?![\w.-]))`
        // boundary is explicitly pinned — only the reserved `:EOF` token is rejected, not
        // labels that merely start with `eof`.
        // `:eof2` / `:eofish` / `:end-of-file` も fixture に含め、`(?!eof(?![\w.-]))` の境界条件を固定する
        // (予約トークン `:EOF` だけが除外され、`eof` で始まる別名は通る)。
        var content = "@echo off\r\nREM Build script\r\nsetlocal\r\n\r\nset VERSION=1.0.0\r\nSET OUTPUT_DIR=%~dp0out\r\nSet /A COUNT=1\r\nSET /P INPUT=Enter: \r\nset \"QUOTED=value with spaces\"\r\n@set AT_PREFIX=1\r\n@ SET AT_SPACED=2\r\nset /a COMPOUND+=1\r\nset /A SHIFTED<<=2\r\nif not defined INLINE_DEF set INLINE_DEF=inline_default\r\nif \"%1\"==\"\" set INLINE_EQ=empty\r\nset CHAIN_A=1 & set CHAIN_B=2\r\nif exist foo.txt set IF_CHAIN_X=3 & set IF_CHAIN_Y=4\r\nif exist foo.txt ( set PAREN_P=5 ) else set ELSE_Q=6\r\nfor %%I in (1) do set LOOPVAR=%%I\r\nREM set FROM_REM=ignored\r\nREM & set FROM_REM_AMP=ignored\r\nREM ( set FROM_REM_PAREN=ignored )\r\nREM else set FROM_REM_ELSE=ignored\r\nREM do set FROM_REM_DO=ignored\r\n@REM & set FROM_AT_REM_AMP=ignored\r\n:: set FROM_DOUBLE_COLON=ignored\r\n:: & set FROM_DC_AMP=ignored\r\n:: ( set FROM_DC_PAREN=ignored )\r\n:: else set FROM_DC_ELSE=ignored\r\n:: do set FROM_DC_DO=ignored\r\n\r\n:main\r\ncall :compile\r\nif errorlevel 1 goto :error\r\ncall :test\r\ngoto :end\r\n\r\n:compile\r\necho Compiling...\r\ndotnet build\r\nexit /b %ERRORLEVEL%\r\n\r\n:test\r\necho Testing...\r\nexit /b %ERRORLEVEL%\r\n\r\n:error\r\necho Build failed\r\ngoto :EOF\r\n\r\n:end\r\ncall :eOf\r\ncall :eof2\r\ncall :eofish\r\ncall :end-of-file\r\ncall :build.release\r\nendlocal\r\n\r\n:eof2\r\nexit /b 0\r\n\r\n:eofish\r\nexit /b 0\r\n\r\n:end-of-file\r\nexit /b 0\r\n\r\n:build.release\r\nexit /b 0\r\n\r\n:: This is a batch comment and must not produce a symbol\r\n::: triple-colon comment must not produce a symbol either\r\n";
        var symbols = SymbolExtractor.Extract(1, "batch", content);

        // Exact function label set — nothing extra (no `:EOF`, no comment-derived names),
        // but the `eof`-prefixed user labels (`eof2`, `eofish`, `end-of-file`) pass, and
        // dotted labels (`build.release`) are captured in full rather than truncated.
        // function ラベル集合は厳密一致 — `:EOF` / コメント由来の偽名は混ざらないが、
        // `eof` で始まるユーザーラベル (`eof2` / `eofish` / `end-of-file`) は通り、
        // ドット付きラベル (`build.release`) も切り詰めず全体が取得される。
        var functionNames = symbols.Where(s => s.Kind == "function").Select(s => s.Name).ToHashSet();
        Assert.Equal(new HashSet<string> { "main", "compile", "test", "error", "end", "eof2", "eofish", "end-of-file", "build.release" }, functionNames);

        // Exact property name set — nothing extra from comments / echo lines, and the new
        // `@set`, compound-operator, and inline-`if` variants all produce a symbol.
        // property 名集合は厳密一致 — コメントや echo 行由来の偽名は混ざらず、新しく対応した
        // `@set` / 複合演算子 / インライン `if` の各形もすべてシンボル化される。
        var propertyNames = symbols.Where(s => s.Kind == "property").Select(s => s.Name).ToHashSet();
        Assert.Equal(
            new HashSet<string>
            {
                "VERSION", "OUTPUT_DIR", "COUNT", "INPUT", "QUOTED",
                "AT_PREFIX", "AT_SPACED",
                "COMPOUND", "SHIFTED",
                "INLINE_DEF", "INLINE_EQ",
                "CHAIN_A", "CHAIN_B",
                "IF_CHAIN_X", "IF_CHAIN_Y",
                "PAREN_P", "ELSE_Q",
                "LOOPVAR",
            },
            propertyNames);
    }

    [Fact]
    public void Extract_Zig_DetectsSymbols()
    {
        var content = "const std = @import(\"std\");\n\npub fn main() !void {\n    std.debug.print(\"hello\", .{});\n}\n\nfn helper(x: u32) u32 {\n    return x + 1;\n}\n\npub const Config = struct {\n    name: []const u8,\n};\n\nconst Direction = enum {\n    north,\n    south,\n};\n\ntest \"basic test\" {\n    try std.testing.expect(true);\n}";
        var symbols = SymbolExtractor.Extract(1, "zig", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "std");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "main" && s.Visibility == "pub");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "helper");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Config" && s.Visibility == "pub");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Direction");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "basic test");
    }

    [Fact]
    public void Extract_Makefile_DetectsTargetsAndAssignments()
    {
        var content = """
            CC := gcc
            CFLAGS ::= -O2 -Wall
            OBJ = foo.o bar.o
            DEBUG ?= 1
            EXTRA += -pipe

            all: program
            program: $(OBJ)
            	$(CC) -o $@ $^

            foo.o: foo.c foo.h
            	$(CC) $(CFLAGS) -c foo.c

            .PHONY: clean install

            clean:
            	rm -f *.o program

            install: all
            	cp program /usr/local/bin

            %.o: %.c
            	$(CC) $(CFLAGS) -c $< -o $@

            $(OBJ): %.o: %.c
            	$(CC) -c $< -o $@
            """;
        var symbols = SymbolExtractor.Extract(1, "makefile", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "all");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "program");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "foo.o");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "clean");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "install");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "%.o");

        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "CC");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "CFLAGS");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "OBJ");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "DEBUG");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "EXTRA");

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "CC");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "CFLAGS");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "OBJ");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "DEBUG");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "EXTRA");
    }

    [Fact]
    public void Extract_Dockerfile_DetectsStages()
    {
        var content = "FROM node:18 AS builder\nWORKDIR /app\nCOPY . .\nRUN npm build\n\nFROM alpine:3.18\nCOPY --from=builder /app/dist /app\n";
        var symbols = SymbolExtractor.Extract(1, "dockerfile", content);

        // Named stages (AS builder) take priority over base image on the same line
        // 同一行では名前付きステージ(AS builder)がベースイメージより優先
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "builder");
        // Unnamed FROM lines produce base image class / 名前なしFROM行はベースイメージclassを生成
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "alpine:3.18");
        Assert.Equal(2, symbols.Count);
    }

    [Fact]
    public void Extract_Protobuf_DetectsSymbols()
    {
        var content = """
            syntax = "proto3";
            import "google/protobuf/timestamp.proto";

            message User {
              string name = 1;
              int32 age = 2;
            }

            enum Status {
              UNKNOWN = 0;
              ACTIVE = 1;
            }

            service UserService {
              rpc GetUser (GetUserRequest) returns (User);
              rpc ListUsers (ListUsersRequest) returns (ListUsersResponse);
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "protobuf", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "google/protobuf/timestamp.proto");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Status");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetUser");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ListUsers");
    }

    [Fact]
    public void Extract_Html_CapturesIdAttributesAsProperties()
    {
        var content = """
            <!DOCTYPE html>
            <html>
              <body>
                <header id="main-header" class="site-header"><h1>Welcome</h1></header>
                <main id='content'><article></article></main>
                <section id="side-panel">legacy</section>
              </body>
            </html>
            """;

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "main-header");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "content");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "side-panel");
    }

    [Fact]
    public void Extract_Html_IgnoresDataIdAndAriaIdAndXmlIdAttributes()
    {
        // `data-id`, `aria-*id`, and `xml:id` must not be captured as plain id attributes.
        // data-id / aria-*id / xml:id を通常の id として拾わない。
        var content = """
            <article data-id="1"></article>
            <div aria-labelledby="x" aria-hiddenid="bogus"></div>
            <span xml:id="ns"></span>
            """;

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "1");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "bogus");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "ns");
    }

    [Fact]
    public void Extract_Html_CapturesExternalScriptAndLinkAsImports()
    {
        var content = """
            <link rel="stylesheet" href="style.css">
            <link rel="icon" href='/favicon.ico'>
            <script src="main.js"></script>
            <script type="module" src='/static/app.mjs'></script>
            <script>inline(); // no src — no import</script>
            """;

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "style.css");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "/favicon.ico");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "main.js");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "/static/app.mjs");
        Assert.DoesNotContain(symbols, s => s.Kind == "import" && s.Name == "inline()");
    }

    [Fact]
    public void Extract_Html_CapturesCustomWebComponentTagsAsClasses()
    {
        // Custom element tag names always contain a hyphen per the HTML spec.
        // HTML 仕様上、カスタム要素名には必ずハイフンが含まれる。
        var content = """
            <my-button>ok</my-button>
            <app-sidebar></app-sidebar>
            <div>plain</div>
            """;

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "my-button");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "app-sidebar");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "div");
    }

    [Fact]
    public void Extract_Html_CapturesAllSymbolsOnSameLine()
    {
        // Minified HTML or a single line with multiple landmark-bearing tags must
        // produce one symbol per match, not only the winning pattern's first hit.
        // Closes #215 codex review blocker.
        // ミニファイされた HTML や 1 行に複数の landmark タグが入るケースでも、
        // 勝ちパターンの 1 件ではなく各マッチごとにシンボルが出る必要がある。
        var content = "<alpha-card id=\"first\"></alpha-card><beta-card id=\"second\"></beta-card>" +
            "<script src=\"a.js\"></script><link rel=\"stylesheet\" href=\"b.css\">";

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "alpha-card");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "beta-card");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "first");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "second");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "a.js");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "b.css");
    }

    [Fact]
    public void Extract_Html_IgnoresSymbolsInsideComments()
    {
        // HTML comments must not produce phantom imports / classes / properties,
        // including multi-line comments. Closes #215 codex review blocker.
        // HTML コメント内のタグ類を phantom シンボルとして拾わないこと。複数行に
        // またがるコメントでも同様。
        var content = """
            <!-- <script src="commented.js"></script> -->
            <article id="real"></article>
            <!--
              <my-widget id="fake"></my-widget>
              <link rel="stylesheet" href="also-commented.css">
            -->
            """;

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "import" && s.Name == "commented.js");
        Assert.DoesNotContain(symbols, s => s.Kind == "import" && s.Name == "also-commented.css");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "my-widget");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "fake");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "real");
    }

    [Fact]
    public void Extract_Html_IgnoresSymbolsInsideScriptAndStyleBodies()
    {
        // Inline <script> body content is raw text per the HTML spec and must not
        // leak symbols from template strings. <style> body text follows the same
        // raw-text rule. Closes #215 codex review blocker.
        // HTML 仕様上、<script> 本体は raw text であり、テンプレート文字列から
        // 疑似シンボルを漏らしてはいけない。<style> 本体も同じ raw text 規則。
        var content = """
            <script>
              const tpl = '<inline-card id="inline-id"></inline-card>';
            </script>
            <style>
              .rule { background: url('<bg-tag id="bg">'); }
            </style>
            <section id="visible"></section>
            """;

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "inline-card");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "inline-id");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "bg-tag");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "bg");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "visible");
    }

    [Fact]
    public void Extract_Html_StillCapturesExternalScriptSrcEvenWhenBodyHasRawText()
    {
        // The masker must preserve the <script src="..."> opening tag so external
        // scripts are still indexed, while raw-text children stay masked.
        // raw-text の子要素はマスクしつつ、<script src="..."> 開始タグは保ち、
        // 外部 script が引き続き import として索引されることを固定する。
        var content = "<script src=\"app.js\">const x = '<evil-tag id=\"evil\"></evil-tag>';</script>";

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "app.js");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "evil-tag");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "evil");
    }

    [Fact]
    public void Extract_Html_CapturesMultiLineScriptAndLinkOpeningTags()
    {
        // Formatter-split opening tags must still match across lines so imports
        // are not silently dropped. Closes #215 codex review finding.
        // フォーマッタによって開始タグが改行されても、import を黙って落とさない
        // ようクロス行で一致する必要がある。#215 codex review 指摘への対応。
        var content = """
            <script
              type="module"
              src="/app.js"></script>
            <link
              rel="stylesheet"
              href="/app.css">
            """;

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "/app.js");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "/app.css");
    }

    [Fact]
    public void Extract_Html_IgnoresSymbolsInsideTextareaAndTitleBodies()
    {
        // <textarea> / <title> bodies are RCDATA per the HTML spec and their
        // contents must not leak phantom symbols. Closes #215 codex review finding.
        // <textarea> / <title> の本体は HTML 仕様上 RCDATA であり、疑似シンボルを
        // 漏らしてはならない。#215 codex review 指摘への対応。
        var content = """
            <textarea><my-widget id="fake"></my-widget></textarea>
            <title><bogus-tag id="phantom"></bogus-tag></title>
            <section id="real"></section>
            """;

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "my-widget");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "fake");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "bogus-tag");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "phantom");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "real");
    }

    [Fact]
    public void Extract_Html_MasksUnclosedRawTextAndRcdataBodies()
    {
        // cdidx indexes the working tree, so unclosed <script> / <style> /
        // <textarea> / <title> are common mid-edit. Phantom symbols from those
        // unclosed bodies must not leak. Closes #215 codex review finding.
        // cdidx は working tree を対象にするため、編集途中で <script> / <style> /
        // <textarea> / <title> が未閉鎖な状態は普通に起きる。未閉鎖でも本体から
        // phantom シンボルを漏らしてはならない。#215 codex review 指摘への対応。
        var unclosedScript = "<script>const tpl = '<evil-card id=\"phantom\"></evil-card>';";
        var unclosedSymbols = SymbolExtractor.Extract(1, "html", unclosedScript);
        Assert.DoesNotContain(unclosedSymbols, s => s.Kind == "class" && s.Name == "evil-card");
        Assert.DoesNotContain(unclosedSymbols, s => s.Kind == "property" && s.Name == "phantom");

        var unclosedStyle = "<style>\n  .r { content: '<rogue-tag id=\"styleid\"></rogue-tag>'; }";
        var unclosedStyleSymbols = SymbolExtractor.Extract(1, "html", unclosedStyle);
        Assert.DoesNotContain(unclosedStyleSymbols, s => s.Kind == "class" && s.Name == "rogue-tag");
        Assert.DoesNotContain(unclosedStyleSymbols, s => s.Kind == "property" && s.Name == "styleid");

        var unclosedTextarea = "<textarea><my-widget id=\"taid\"></my-widget>";
        var unclosedTextareaSymbols = SymbolExtractor.Extract(1, "html", unclosedTextarea);
        Assert.DoesNotContain(unclosedTextareaSymbols, s => s.Kind == "class" && s.Name == "my-widget");
        Assert.DoesNotContain(unclosedTextareaSymbols, s => s.Kind == "property" && s.Name == "taid");

        var unclosedTitle = "<title><bogus-tag id=\"titleid\"></bogus-tag>";
        var unclosedTitleSymbols = SymbolExtractor.Extract(1, "html", unclosedTitle);
        Assert.DoesNotContain(unclosedTitleSymbols, s => s.Kind == "class" && s.Name == "bogus-tag");
        Assert.DoesNotContain(unclosedTitleSymbols, s => s.Kind == "property" && s.Name == "titleid");
    }

    [Fact]
    public void Extract_Html_MultiLineOpeningTagReportsAttributeValueLine()
    {
        // When a `<script>` / `<link>` opening tag wraps across lines, the symbol's
        // line must point at the line that actually carries the attribute value,
        // not the opening `<`, so `definition` / `excerpt` jump to the right place.
        // Closes #215 codex review finding.
        // 開始タグが折り返された場合、symbol の行は開始 `<` の行ではなく属性値が
        // 実在する行を指す必要がある。こうしないと `definition` / `excerpt` の
        // ジャンプ先が先頭行にずれる。#215 codex review 指摘への対応。
        var content = """
            <script
              type="module"
              src="/app.js"></script>
            <link
              rel="stylesheet"
              href="/app.css">
            """;

        var symbols = SymbolExtractor.Extract(1, "html", content);

        var scriptImport = Assert.Single(symbols, s => s.Kind == "import" && s.Name == "/app.js");
        Assert.Equal(3, scriptImport.Line);
        Assert.Equal(3, scriptImport.StartLine);

        var linkImport = Assert.Single(symbols, s => s.Kind == "import" && s.Name == "/app.css");
        Assert.Equal(6, linkImport.Line);
        Assert.Equal(6, linkImport.StartLine);
    }

    [Fact]
    public void Extract_Html_IgnoresIdLiteralsInDocumentText()
    {
        // Prose like `<p>documentation says id="fake" here</p>` must not be
        // harvested as a DOM id — the `id=` pattern only matters inside an
        // opening tag. Real `<section id="real">` still captures.
        // Closes #215 codex review finding.
        // 本文中の `id="..."` を DOM id として誤抽出してはならない。id 属性は開始タグ
        // の内部でのみ意味を持つ。実在する `<section id="real">` は引き続き抽出する。
        // #215 codex review 指摘への対応。
        var content = """
            <p>documentation says id="fake" here</p>
            <article>inline prose id='phantom' mentioned</article>
            <section id="real"></section>
            """;

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "fake");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "phantom");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "real");
    }

    [Fact]
    public void Extract_Html_MasksUnclosedComments()
    {
        // Unclosed `<!--` must also mask its suffix to EOF, matching the
        // raw-text / RCDATA `|\z` policy. Without this, editing a comment
        // live leaked every tag inside the unclosed comment as phantom
        // symbols. Closes #215 codex review finding.
        // `<!--` 未閉鎖でも EOF まで本体をマスクする必要がある。raw-text / RCDATA と
        // 同じく編集途中の未閉鎖コメントから phantom シンボルを漏らさないためのもの。
        // #215 codex review 指摘への対応。
        var content = "<!--\n<script src=\"commented.js\"></script>\n<custom-tag id=\"phantom\"></custom-tag>";

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "import" && s.Name == "commented.js");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "custom-tag");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "phantom");
    }

    [Fact]
    public void Extract_Html_CommentLiteralInsideScriptDoesNotSwallowFollowingTags()
    {
        // `<script>` bodies that literally contain `<!--` must not be treated
        // as unclosed comments. The raw-text masker has to run before the
        // comment masker or everything after the literal gets blanked out,
        // dropping every real subsequent symbol. Closes #215 codex review finding.
        // `<script>` 本文に `<!--` リテラルが入っているだけで未閉鎖コメント扱いに
        // してはならない。body マスクをコメントマスクより先に動かさないと、リテラル
        // 以降の本物のタグまで全滅する。#215 codex review 指摘への対応。
        var content = "<script>const s = \"<!--\";</script>\n<section id=\"real\"></section>\n<my-widget></my-widget>\n<link href=\"/app.css\">";

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "real");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "my-widget");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "/app.css");
    }

    [Fact]
    public void Extract_Html_CapturesUnquotedAttributeValues()
    {
        // HTML5 allows unquoted attribute values. Dropping these silently meant
        // `<section id=real>`, `<script src=/app.js>`, `<link href=/app.css>`
        // all produced zero symbols. Closes #215 codex review finding.
        // HTML5 では引用符なし属性値も許される。黙って無視していた結果
        // `<section id=real>` / `<script src=/app.js>` / `<link href=/app.css>` が
        // いずれも 0 シンボルを返していた。#215 codex review 指摘への対応。
        var content = "<section id=real></section>\n<script src=/app.js></script>\n<link rel=stylesheet href=/app.css>";

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "real");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "/app.js");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "/app.css");
    }

    [Fact]
    public void Extract_Html_IgnoresAttributeLookAlikesInsideSameTagQuotedValues()
    {
        // The `<script src=...>` / `<link href=...>` / `id=...` regexes anchor at the real
        // outer tag so their `[^>]*?` prefix can reach forward into the same opening tag's
        // other attribute values. Without checking the name capture position, literals like
        // `data-note="src=evil.js"` on a real `<script>` leaked phantom imports. Pin that
        // the name-capture mask catches these same-tag leaks. Closes #215 codex review finding.
        // `<script src=...>` / `<link href=...>` / `id=...` の regex は実在する外側タグ
        // 起点で走り、`[^>]*?` が同じ開始タグ内の別属性値へ進めてしまう。開始 `<` だけで
        // 判定すると `<script data-note="src=evil.js">` のような同一タグ内の属性リテラル
        // から phantom import が漏れるので、name capture 位置で mask をかけ直し、それが
        // 同一タグ内の src=/href=/id= 文字列にも効くことを固定する。#215 codex review
        // 指摘への対応。
        var content = "<script data-note=\"src=evil.js\"></script>\n<link title=\"href=evil.css\" rel=stylesheet href=\"/real.css\">\n<div title=\"docs id=phantom\"></div>\n<section id=\"real\"></section>";

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "import" && s.Name == "evil.js");
        Assert.DoesNotContain(symbols, s => s.Kind == "import" && s.Name == "evil.css");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "phantom");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "/real.css");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "real");
    }

    [Fact]
    public void Extract_Html_CapturesIdValuesWithPunctuation()
    {
        // Quoted id values can legally contain any non-whitespace character per the HTML5
        // id attribute spec. The previous `[\w:.\-]+` class silently dropped real DOM
        // anchors like `id="user@top"` and `id="group/main"`, so `definition` / `outline`
        // couldn't jump to them. Pin the broadened quoted class while keeping
        // unquoted values conservative (they still collide with CSS selector syntax).
        // Closes #215 codex review finding.
        // HTML5 では引用符付き id 値に任意の non-whitespace 文字が使える。従来の
        // `[\w:.\-]+` クラスだと `id="user@top"` / `id="group/main"` のような実在の
        // DOM アンカーを黙って落としていた。引用符付きは受け入れを広げつつ、
        // 引用符なしは CSS セレクタ構文との衝突を避けて保守的なままにする。
        // #215 codex review 指摘への対応。
        var content = "<section id=\"user@top\"></section>\n<section id=\"group/main\"></section>\n<section id=\"plain.id\"></section>";

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "user@top");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "group/main");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "plain.id");
    }

    [Fact]
    public void Extract_Html_QuotedGtInsideScriptOpenTagPreservesSiblingSymbols()
    {
        // A `>` character is legal inside a quoted attribute value, so the raw-text
        // body masker must parse the `<script>` opening tag with quote awareness.
        // The earlier `[^>]*>` class terminated at the first quoted `>` and blanked
        // every following real attribute and sibling tag as masked body content,
        // which dropped both the intended `src="/app.js"` import and the sibling
        // `<section id="real">`. Closes #215 codex review blocker.
        // 引用符付き属性値内の `>` でも raw-text 本体マスクの開始タグ解析が終端しない
        // ことを固定する。以前の `[^>]*>` は先頭の引用符内 `>` でタグを切ってしまい、
        // 後続の実属性と兄弟タグを body としてマスクしていたため、本来 emit すべき
        // `src="/app.js"` の import と `<section id="real">` の両方を落としていた。
        // #215 codex review blocker 対応。
        var content = "<script data-note=\"a > b\" src=\"/app.js\"></script>\n<section id=\"real\"></section>";

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "/app.js");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "real");
    }

    [Fact]
    public void Extract_Html_UnterminatedQuotedAttributeDoesNotSwallowRestOfFile()
    {
        // Mid-edit working-tree content commonly leaves a quoted attribute unterminated
        // (e.g. user is still typing `title="...`). When no valid-looking close exists
        // to EOF, the parser must bound damage by bailing at the current line rather
        // than scanning through every subsequent sibling tag looking for a matching
        // quote. Otherwise every `<my-widget>` / `<link href=...>` after the broken
        // tag drops out of `symbols` / `definition` / `outline` until the user types
        // the matching quote. Closes #215 codex review finding.
        // 編集中の working tree では `title="...` のような未閉鎖引用符が頻発する。
        // EOF まで妥当な閉じ候補が無い真の未終端では、行末で止めて以降の
        // `<my-widget>` / `<link href=...>` を symbols / definition / outline から
        // 消さないこと。#215 codex review 指摘対応。
        var content = "<div title=\"oops\n<my-widget></my-widget>\n<link href=/app.css>";

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "my-widget");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "/app.css");
    }

    [Fact]
    public void Extract_Html_MultiLineQuotedAttributeValueWithEmbeddedTagsPreservesSiblingAttributes()
    {
        // HTML5 allows newlines AND tag-like content inside quoted attribute values
        // (`<div title="line1\n<section></section>\nline3" id="real">`). The earlier
        // `\n<tagstart>` bail heuristic treated any `\n<` inside a quoted value as an
        // unterminated-quote signal, which silently prematurely terminated valid
        // multi-line title / data-note / alt values and either (1) leaked embedded
        // `<section id=phantom>` as a phantom `property phantom` symbol, or (2)
        // dropped the genuine `id="real"` attribute that followed the value. Pin
        // that valid multi-line quoted values containing tag-like content are
        // treated as single attribute values, and the following `id="real"` is
        // emitted. Closes #215 codex review #9 blocker 2.
        // HTML5 は引用符付き属性値の中に改行もタグ様テキストも許容する
        // (`<div title="line1\n<section></section>\nline3" id="real">`)。以前の
        // `\n<tagstart>` 早期中断ヒューリスティクスは、これを未終端と誤認して
        // (1) 埋め込まれた `<section id=phantom>` を phantom な property として拾ったり、
        // (2) 後続する本物の `id="real"` を落としたりしていた。妥当な複数行引用属性値を
        // 正しく 1 つの値として扱い、後続の `id="real"` を emit することを固定する。
        // #215 codex review #9 blocker 2 対応。
        var content = "<div title=\"line1\n<section id=phantom></section>\nline3\" id=\"real\"></div>";

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "real");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "phantom");
    }

    [Fact]
    public void Extract_Html_RawTextOpenerInsideQuotedAttributeValueDoesNotMaskFollowingContent()
    {
        // `<script>` / `<style>` / `<textarea>` / `<title>` embedded inside another
        // tag's quoted attribute value is NOT a real raw-text opener. The mask pass
        // must walk past the outer tag's quoted value rather than re-encountering
        // `<script>` / `<!--` inside the value and masking through EOF. Pin that
        // quoted `<script>` / `<!--` does not swallow sibling tags on following lines.
        // Closes #215 codex review #9 blocker 1.
        // 引用符付き属性値内に出てくる `<script>` / `<style>` / `<textarea>` / `<title>`
        // や `<!--` は raw-text 開始でもコメント開始でもない。マスク処理は外側タグの
        // 引用符付き値を飛ばして進まなければならず、さもないと値内の `<script>` を
        // raw-text 開始と誤認して EOF までマスクし、後続の兄弟タグを全部落とす。
        // #215 codex review #9 blocker 1 対応。
        var scriptInAttr = "<div title=\"<script>\">ok</div>\n<section id=\"real\"></section>";
        var symbols1 = SymbolExtractor.Extract(1, "html", scriptInAttr);
        Assert.Contains(symbols1, s => s.Kind == "property" && s.Name == "real");

        var commentInAttr = "<div title=\"<!--\">ok</div>\n<section id=\"realc\"></section>";
        var symbols2 = SymbolExtractor.Extract(1, "html", commentInAttr);
        Assert.Contains(symbols2, s => s.Kind == "property" && s.Name == "realc");
    }

    [Fact]
    public void Extract_Html_SelfClosingVoidElementDoesNotDropSiblingAttributes()
    {
        // XHTML / formatter-wrapped HTML often writes void elements as
        // `<link href="/app.css"/>`. Previously the closing `"` of the path-
        // like attribute had post-context `/` which was NOT accepted as a
        // strong close, and the nested-attribute fallback then mis-identified
        // `id="real"` on the following sibling tag as a nested opener, causing
        // FindHtmlQuoteClose to return -1 and the attribute parser to bail,
        // dropping BOTH the `href` import and the sibling `id`. The self-
        // closing shape `"/>` is now accepted as a strong post-context.
        // Closes #215 codex review #11 Blocker 1.
        // XHTML / フォーマッタ折り返しの HTML では `<link href="/app.css"/>` の
        // 形を採ることがある。以前は閉じ `"` 直後が `/` のため strong でなく、
        // 後続の `id="real"` を nested 属性と誤認して -1 → 行末 bail となり、
        // `href` import も兄弟 `id` も落としていた。`"/>` を strong として
        // 受理することで self-closing void 要素も後続も正しく拾う。
        // #215 codex review #11 Blocker 1 対応。
        var content = "<link href=\"/app.css\"/><section id=\"real\"></section>";

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "/app.css");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "real");
    }

    [Fact]
    public void Extract_Html_CdataAndProcessingInstructionContentsAreNotLeakedAsSymbols()
    {
        // XHTML / SVG / MathML content can contain `<![CDATA[...]]>` sections
        // whose body is text, not markup. The old `<!`-branch in the extractor
        // stopped at the first `>` which is often inside an inner element of
        // the CDATA body, so the remainder was parsed as real HTML and leaked
        // phantom tags / properties. Processing instructions `<?...?>` and
        // DOCTYPE declarations have the same shape. Pin that CDATA / PI /
        // DOCTYPE bodies do NOT emit symbols and that siblings after them
        // still do. Closes #215 codex review #11 Blocker 2.
        // XHTML / SVG / MathML の `<![CDATA[...]]>` 本体はマークアップではなく
        // テキスト。旧実装は最初の `>` で終端扱いしたため、内部要素の `>` で
        // 早期終了して残り本体が real HTML として抽出され phantom を漏らして
        // いた。`<?...?>` や `<!DOCTYPE...>` も同様。CDATA / PI / DOCTYPE 本体
        // は emit せず、それらの後続兄弟が emit されることを固定する。
        // #215 codex review #11 Blocker 2 対応。
        var cdata = "<![CDATA[ <two-widget id=\"phantom\"></two-widget><three-widget id=\"ghost\"></three-widget> ]]><section id=\"real\"></section>";
        var symbolsCdata = SymbolExtractor.Extract(1, "html", cdata);
        Assert.DoesNotContain(symbolsCdata, s => s.Name == "two-widget");
        Assert.DoesNotContain(symbolsCdata, s => s.Name == "three-widget");
        Assert.DoesNotContain(symbolsCdata, s => s.Name == "phantom");
        Assert.DoesNotContain(symbolsCdata, s => s.Name == "ghost");
        Assert.Contains(symbolsCdata, s => s.Kind == "property" && s.Name == "real");

        var pi = "<?xml version=\"1.0\"?><?xml-stylesheet href=\"/evil.css\"?><section id=\"realpi\"></section>";
        var symbolsPi = SymbolExtractor.Extract(1, "html", pi);
        Assert.DoesNotContain(symbolsPi, s => s.Kind == "import" && s.Name == "/evil.css");
        Assert.Contains(symbolsPi, s => s.Kind == "property" && s.Name == "realpi");

        var doctype = "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\"><section id=\"realdt\"></section>";
        var symbolsDt = SymbolExtractor.Extract(1, "html", doctype);
        Assert.Contains(symbolsDt, s => s.Kind == "property" && s.Name == "realdt");
    }

    [Fact]
    public void Extract_Html_UnterminatedOuterTagWithEmbeddedRawTextOpenerDoesNotMaskToEof()
    {
        // codex review #10 Blocker B: when the outer non-raw-text tag is itself
        // mid-edit and never closes (no matching `"` anywhere), the mask pass
        // must NOT re-enter the raw-text / comment branch at the `<!--` /
        // `<script>` sitting inside the broken quoted value, because doing so
        // masks through EOF and drops every sibling tag on the following lines.
        // Advancing past the current line when a non-raw-text opener cannot be
        // closed lets the later sibling tags still be walked. Closes #215 codex
        // review #10 Blocker B.
        // 外側の non-raw-text タグ自体が編集途中で EOF まで `"` が現れない場合、
        // 破損した引用値の中の `<!--` / `<script>` を comment / raw-text 開始と
        // 再解釈して EOF までマスクしてはならない。未終端 opener は現在行で
        // 止めて次行以降の兄弟タグを拾う。#215 codex review #10 Blocker B 対応。
        var commentOpenerInBrokenTag = "<div title=\"<!--\n<my-widget></my-widget>\n<link href=/app.css>";
        var symbols1 = SymbolExtractor.Extract(1, "html", commentOpenerInBrokenTag);
        Assert.Contains(symbols1, s => s.Kind == "class" && s.Name == "my-widget");
        Assert.Contains(symbols1, s => s.Kind == "import" && s.Name == "/app.css");

        var scriptOpenerInBrokenTag = "<div title=\"<script>\n<my-widget></my-widget>\n<link href=/app.css>";
        var symbols2 = SymbolExtractor.Extract(1, "html", scriptOpenerInBrokenTag);
        Assert.Contains(symbols2, s => s.Kind == "class" && s.Name == "my-widget");
        Assert.Contains(symbols2, s => s.Kind == "import" && s.Name == "/app.css");
    }

    [Fact]
    public void Extract_Html_IgnoresNativeHyphenatedSvgAndMathmlTags()
    {
        // HTML/SVG/MathML have a small set of native hyphenated tag names
        // (`<font-face>`, `<color-profile>`, `<missing-glyph>`, `<annotation-xml>`).
        // Per the HTML spec these are reserved and must NOT be treated as custom
        // elements; otherwise any project with inline SVG / MathML gets phantom
        // `class` symbols. Pin that genuine custom elements next to reserved tags
        // are still captured. Closes #215 codex review finding.
        // HTML / SVG / MathML にはハイフン付きだが仕様で予約された標準タグ（`<font-face>`
        // / `<color-profile>` / `<missing-glyph>` / `<annotation-xml>`）が存在する。
        // これらを custom element 扱いしないこと、および同居する本物のカスタム要素は
        // 引き続き class として拾うことを固定する。#215 codex review 指摘対応。
        var content = "<svg><font-face></font-face><color-profile></color-profile><missing-glyph></missing-glyph><my-widget></my-widget></svg>\n<math><annotation-xml></annotation-xml></math>\n<app-sidebar></app-sidebar>";

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "font-face");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "color-profile");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "missing-glyph");
        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "annotation-xml");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "my-widget");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "app-sidebar");
    }

    [Fact]
    public void Extract_Html_MultiLineQuotedAttributeValuePreservesFollowingAttributes()
    {
        // HTML5 allows newlines inside quoted attribute values. Formatter-wrapped
        // tags and verbose `title` / `alt` / `data-note` copy often span multiple
        // lines. The state machine must NOT abort tag parsing at the first
        // newline inside a quoted value — otherwise it silently drops sibling
        // `src=` / `href=` / `id=` attributes on the same tag. Closes #215
        // codex review #8 blocker.
        // HTML5 は引用符付き属性値の中に改行を許容する。フォーマッタによる折り返し
        // タグや長文の `title` / `alt` / `data-note` 等は複数行に跨ることがある。
        // state machine は改行で属性解析を中断してはならない — さもないと同一タグ内の
        // `src=` / `href=` / `id=` が兄弟属性として silent に落ちる。#215 codex
        // review #8 blocker 対応。
        var content = "<div title=\"line1\nline2\" id=\"real\">text</div>\n<link data-note=\"line1\nline2\" href=\"/app.css\">\n<script data-note=\"line1\nline2\" src=\"/app.js\"></script>";

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "real");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "/app.css");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "/app.js");
    }

    [Fact]
    public void Extract_Html_UnterminatedQuoteInRawTextOpenerDoesNotLeakScriptBodySymbols()
    {
        // Mid-edit `<script>` / `<style>` / `<textarea>` / `<title>` openers with
        // an unterminated quoted attribute MUST still have their body masked,
        // otherwise the state machine walks into what should be raw-text /
        // RCDATA content and emits phantom `class` / `property` / `import`
        // symbols from embedded template-string markup. The mask falls back to
        // EOF when the opener cannot be closed, matching HTML's raw-text spec
        // behavior. Closes #215 codex review #8 blocker.
        // 編集中の `<script>` 等の開始タグで引用符が未終端の場合も、本体は必ず
        // マスクされなければならない。さもないと state machine が raw-text / RCDATA
        // 本体に入り込み、埋め込まれたテンプレート文字列のタグ風テキストから phantom
        // シンボルを漏らす。未終端時は仕様どおり EOF までマスクする。#215 codex
        // review #8 blocker 対応。
        var content = "<script data-note=\"oops\nconst tpl = '<evil-card id=\"phantom\"></evil-card>';\n<section id=\"real\"></section>";

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "evil-card");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "phantom");
        // The `<section id="real">` after the unterminated `<script>` is inside
        // the unclosed raw-text body per spec, so it is intentionally NOT
        // emitted — this matches how a browser would treat the content.
        // 仕様上 `<section id="real">` も未閉鎖 raw-text の中なので、ブラウザと同じく
        // emit しないのが正しい。
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "real");
    }

    [Fact]
    public void Extract_Html_IgnoresSymbolsNestedInsideQuotedAttributeValues()
    {
        // Tag-looking text embedded inside a quoted attribute value (commonly in
        // doc generators, Markdown-to-HTML output, or `title="..."` blurbs) must
        // not produce phantom custom-element, id, src, or href symbols. Closes
        // #215 codex review finding.
        // 引用符付き属性値の中に入ったタグ風テキスト（ドキュメント生成器や
        // `title="..."` の注釈によくある）から、phantom な custom element / id /
        // src / href を拾ってはならない。#215 codex review 指摘への対応。
        var content = "<div title=\"<fake-widget>\" data-doc=\"<section id=phantom></section>\" aria-label=\"<script src=/evil.js></script><link href=/evil.css>\"></div>";

        var symbols = SymbolExtractor.Extract(1, "html", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "class" && s.Name == "fake-widget");
        Assert.DoesNotContain(symbols, s => s.Kind == "property" && s.Name == "phantom");
        Assert.DoesNotContain(symbols, s => s.Kind == "import" && s.Name == "/evil.js");
        Assert.DoesNotContain(symbols, s => s.Kind == "import" && s.Name == "/evil.css");
    }

    [Fact]
    public void Extract_PythonTripleQuotedString_DoesNotLeakPhantomSymbols()
    {
        // Regression for issue #291: code-shaped fixture text inside """...""" /
        // '''...''' / r"""...""" must not produce phantom class/function rows.
        // issue #291 回帰: """...""" / '''...''' / r"""...""" 内のコード風のフィクスチャ
        // テキストは、phantom の class/function を生成してはならない。
        const string content = """"
            FIXTURE_DOUBLE = """
            class FakeDouble:
                def method_in_double(self): pass
            """

            FIXTURE_SINGLE = '''
            class FakeSingle:
                def method_in_single(self): pass
            '''

            FIXTURE_RAW = r"""
            def raw_fake():
                pass
            """

            class RealClass:
                def real_method(self):
                    pass
            """";

        var symbols = SymbolExtractor.Extract(1, "python", content);

        Assert.DoesNotContain(symbols, s => s.Name == "FakeDouble");
        Assert.DoesNotContain(symbols, s => s.Name == "FakeSingle");
        Assert.DoesNotContain(symbols, s => s.Name == "method_in_double");
        Assert.DoesNotContain(symbols, s => s.Name == "method_in_single");
        Assert.DoesNotContain(symbols, s => s.Name == "raw_fake");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "RealClass");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "real_method");
    }

    [Fact]
    public void Extract_RustRawString_DoesNotLeakPhantomSymbols()
    {
        // Regression for issue #291: code-shaped fixture text inside r#"..."# /
        // r##"..."## raw strings must not produce phantom fn/struct rows.
        // issue #291 回帰: r#"..."# / r##"..."## raw string 内のコード風フィクスチャ
        // テキストは phantom の fn/struct を生成してはならない。
        const string content = "const BASIC: &str = r#\"\n"
            + "fn fake_basic() {}\n"
            + "struct FakeStructBasic;\n"
            + "\"#;\n"
            + "const NESTED: &str = r##\"\n"
            + "contains \"# marker\n"
            + "fn fake_nested() {}\n"
            + "\"##;\n"
            + "fn real_fn() {}\n"
            + "struct RealStruct;\n";

        var symbols = SymbolExtractor.Extract(1, "rust", content);

        Assert.DoesNotContain(symbols, s => s.Name == "fake_basic");
        Assert.DoesNotContain(symbols, s => s.Name == "FakeStructBasic");
        Assert.DoesNotContain(symbols, s => s.Name == "fake_nested");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "real_fn");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "RealStruct");
    }

    [Fact]
    public void Extract_JsTsTemplateLiteral_DoesNotLeakPhantomSymbols()
    {
        // Regression for issue #291: code-shaped fixture text inside multi-line
        // JavaScript/TypeScript `...` template literal bodies must not produce
        // phantom function/class rows. Interpolation hole contents remain visible
        // to downstream reference extraction (covered in ReferenceExtractor tests).
        // issue #291 回帰: 複数行 JavaScript/TypeScript `...` テンプレートリテラル本体の
        // コード風テキストは phantom の function/class を生成してはならない。
        const string content = """
            const src = `
            function fakeFromTemplate() {
              return 1;
            }
            class FakeClassInTemplate {}
            `;

            function realFunction() {}
            class RealClass {}
            """;

        var jsSymbols = SymbolExtractor.Extract(1, "javascript", content);
        Assert.DoesNotContain(jsSymbols, s => s.Name == "fakeFromTemplate");
        Assert.DoesNotContain(jsSymbols, s => s.Name == "FakeClassInTemplate");
        Assert.Contains(jsSymbols, s => s.Kind == "function" && s.Name == "realFunction");
        Assert.Contains(jsSymbols, s => s.Kind == "class" && s.Name == "RealClass");

        var tsSymbols = SymbolExtractor.Extract(1, "typescript", content);
        Assert.DoesNotContain(tsSymbols, s => s.Name == "fakeFromTemplate");
        Assert.DoesNotContain(tsSymbols, s => s.Name == "FakeClassInTemplate");
        Assert.Contains(tsSymbols, s => s.Kind == "function" && s.Name == "realFunction");
        Assert.Contains(tsSymbols, s => s.Kind == "class" && s.Name == "RealClass");
    }

    [Fact]
    public void Extract_CSharp_WrappedStaticConstructor_EmitsOnceAtNameLine()
    {
        // Regression for issue #348: when `static` sits on its own physical line above
        // the constructor name, the extractor must still emit the ctor exactly once,
        // anchored at the name line, with a signature that reflects the full declaration.
        // issue #348 の回帰: `static` が constructor 名の物理行の一つ上に単独で置かれた
        // 場合でも、名前行を起点に重複なく 1 件だけ emit し、signature には宣言全体が
        // 含まれる必要がある。
        const string content = """
            namespace WrappedCtor;

            public class A
            {
                static
                A() { _x = 1; }

                private static int _x;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var ctors = symbols.Where(s => s.Kind == "function" && s.Name == "A").ToList();
        Assert.Single(ctors);
        Assert.Equal(6, ctors[0].Line);
        Assert.Contains("static A()", ctors[0].Signature);
    }

    [Fact]
    public void Extract_CSharp_WrappedInstanceConstructor_EmitsOnceAtNameLine()
    {
        // issue #348 の回帰: `public` 等の可視性モディファイアが単独行に置かれた
        // non-static constructor も同じく名前行で 1 件だけ emit する。
        const string content = """
            namespace WrappedCtor;

            public class B
            {
                public
                B() { _y = 1; }

                private int _y;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var ctors = symbols.Where(s => s.Kind == "function" && s.Name == "B").ToList();
        Assert.Single(ctors);
        Assert.Equal(6, ctors[0].Line);
        Assert.Contains("public B()", ctors[0].Signature);
    }

    [Fact]
    public void Extract_CSharp_WrappedAllmanStaticConstructor_CapturesBody()
    {
        // issue #348 の回帰: Allman スタイル（`static` 単独行 → 名前行 → `{` 単独行）の
        // static constructor も 1 件だけ emit し、本体の閉じ brace まで range を追跡する。
        const string content = """
            namespace WrappedCtor;

            public class F
            {
                static
                F()
                {
                    _u = 1;
                }

                private static int _u;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var ctors = symbols.Where(s => s.Kind == "function" && s.Name == "F").ToList();
        Assert.Single(ctors);
        Assert.Equal(6, ctors[0].Line);
        Assert.Contains("static F()", ctors[0].Signature);
        Assert.True(ctors[0].BodyEndLine >= 9, $"expected body to reach closing brace on line 9 or later, got {ctors[0].BodyEndLine}");
    }

    [Fact]
    public void Extract_CSharp_AttributedWrappedStaticConstructor_EmitsOnceAtNameLine()
    {
        // issue #348 の回帰: 属性行が挟まった wrapped static ctor でも、attribute 行は
        // モディファイア連結に混入せず、名前行 1 件だけに集約される。
        const string content = """
            namespace WrappedCtor;

            public class D
            {
                [System.Obsolete]
                static
                D() { _z = 1; }

                private static int _z;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var ctors = symbols.Where(s => s.Kind == "function" && s.Name == "D").ToList();
        Assert.Single(ctors);
        Assert.Equal(7, ctors[0].Line);
        Assert.Contains("static D()", ctors[0].Signature);
    }

    [Fact]
    public void Extract_CSharp_MultiModifierWrappedConstructor_EmitsOnceAtNameLine()
    {
        // issue #348 の回帰: 複数のモディファイアが別々の物理行に折り返された wrapped ctor
        // （例: `public\nstatic\nE()`）でも、名前行 1 件だけ emit し、signature には両方の
        // モディファイアを保持する。単純 prefix 合成では constructor regex も static ctor
        // regex も受け付けない合成行になるため、static / visibility の variant を試す候補
        // 列挙ロジックが無いと無言で落ちていた。
        const string content = """
            namespace WrappedCtor;

            public class E
            {
                public
                static
                E() { _w = 1; }

                private static int _w;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var ctors = symbols.Where(s => s.Kind == "function" && s.Name == "E").ToList();
        Assert.Single(ctors);
        Assert.Equal(7, ctors[0].Line);
        Assert.Contains("public static E()", ctors[0].Signature);
    }

    [Fact]
    public void Extract_CSharp_CompositeVisibilityWrappedConstructor_EmitsOnceAtNameLine()
    {
        // issue #348 の回帰: 複合 visibility (`protected internal` / `private protected`) が
        // 2 行に分割されて折り返された wrapped ctor でも、名前行 1 件だけ emit し、signature
        // には複合 visibility を保持する。candidate 列挙の先頭に full prefix (`protected internal`)
        // を yield するため、constructor regex の `protected\s+internal` 選択肢でそのまま一致する。
        const string content = """
            namespace WrappedCtor;

            public class F
            {
                protected
                internal
                F() { _v = 1; }

                private static int _v;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var ctors = symbols.Where(s => s.Kind == "function" && s.Name == "F").ToList();
        Assert.Single(ctors);
        Assert.Equal(7, ctors[0].Line);
        Assert.Contains("protected internal F()", ctors[0].Signature);
        Assert.Equal("protected internal", ctors[0].Visibility);
    }

    [Fact]
    public void Extract_CSharp_VisibilityStaticExternWrappedConstructor_EmitsOnceAtNameLine()
    {
        // issue #348 の回帰: visibility + static + extern の 3 modifier が全て別行に折り返された
        // wrapped ctor でも、名前行 1 件だけ emit し、signature には 3 modifier 全てを保持する。
        // full prefix (`public static extern`) は constructor regex の visibility スロット後に
        // static を置けないため単体では通らず、static variant も `()` 要求で引数付きを弾くので
        // 通らないが、visibility-only variant (`public`) が constructor regex にヒットし、signature
        // は full prefix 側から補完される。
        const string content = """
            namespace WrappedCtor;

            public class G
            {
                public
                static
                extern
                G(string s);
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var ctors = symbols.Where(s => s.Kind == "function" && s.Name == "G").ToList();
        Assert.Single(ctors);
        Assert.Equal(8, ctors[0].Line);
        Assert.Contains("public static extern G(string s)", ctors[0].Signature);
    }

    [Fact]
    public void Extract_JavaScript_DetectsClassFieldArrowFunction()
    {
        var content = """
            class Foo {
                handleClick = () => { };
                handleHover = (e) => { return e; };
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        var handleClick = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "handleClick");
        Assert.NotNull(handleClick);
        Assert.Equal("class", handleClick.ContainerKind);
        Assert.Equal("Foo", handleClick.ContainerName);

        var handleHover = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "handleHover");
        Assert.NotNull(handleHover);
        Assert.Equal("class", handleHover.ContainerKind);
        Assert.Equal("Foo", handleHover.ContainerName);
    }

    [Fact]
    public void Extract_TypeScript_DetectsClassFieldArrowFunctionWithTypes()
    {
        var content = """
            class Foo {
                handleClick = (): void => { };
                transform = <T>(x: T): T => { return x; };
                count: number = 0;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var handleClick = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "handleClick");
        Assert.NotNull(handleClick);
        Assert.Equal("class", handleClick.ContainerKind);
        Assert.Equal("Foo", handleClick.ContainerName);
        Assert.Equal("void", handleClick.ReturnType);

        var transform = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "transform");
        Assert.NotNull(transform);
        Assert.Equal("T", transform.ReturnType);

        // Plain value field (no arrow) must not be mis-classified as a function.
        // 素の値フィールド（アロー関数ではない）は function として検出してはならない。
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "count");
    }

    [Fact]
    public void Extract_JavaScript_DetectsClassFieldArrowWithExpressionBody()
    {
        var content = """
            class Foo {
                handleExpr = () => 42;
                compute = (x) => x + 1;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        var handleExpr = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "handleExpr");
        Assert.NotNull(handleExpr);
        Assert.Equal("class", handleExpr.ContainerKind);
        Assert.Equal("Foo", handleExpr.ContainerName);

        var compute = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "compute");
        Assert.NotNull(compute);
        Assert.Equal("class", compute.ContainerKind);
    }

    [Fact]
    public void Extract_TypeScript_DetectsClassFieldArrowWithMultiLineExpressionBody()
    {
        // Regression for multi-line expression body causing scanner to skip the next field.
        // 複数行の式本体が次の field をスキップさせる回帰の回帰テスト。
        var content = """
            class Foo {
                handleExpr = (): number => 42;
                transform = <T>(x: T): T =>
                    x;
                runInline = (a: number, b: number): number => a + b;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var handleExpr = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "handleExpr");
        Assert.NotNull(handleExpr);
        Assert.Equal("class", handleExpr.ContainerKind);
        Assert.Equal("number", handleExpr.ReturnType);

        var transform = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "transform");
        Assert.NotNull(transform);
        Assert.Equal("T", transform.ReturnType);

        // runInline must still be captured after transform's multi-line expression body.
        // transform の複数行式本体の後でも runInline は取りこぼされてはならない。
        var runInline = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "runInline");
        Assert.NotNull(runInline);
        Assert.Equal("class", runInline.ContainerKind);
        Assert.Equal("number", runInline.ReturnType);
    }

    [Fact]
    public void Extract_JavaScript_DoesNotMisclassifyPlainClassFieldAsFunction()
    {
        var content = """
            class Foo {
                value = 42;
                items = [1, 2, 3];
                config = { key: "value" };
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "value");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "items");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "config");
    }

    [Fact]
    public void Extract_JavaScript_DetectsTopLevelGeneratorFunctions()
    {
        var content = """
            function* regularGen() { yield 1; }
            async function* asyncGen() { yield 1; }
            function* spacedGen () { yield 1; }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "regularGen");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "asyncGen");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "spacedGen");
    }

    [Fact]
    public void Extract_TypeScript_DetectsTopLevelGeneratorFunctions()
    {
        var content = """
            function* regularGen(): Generator<number> { yield 1; }
            async function* asyncGen(): AsyncGenerator<number> { yield 1; }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var regular = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "regularGen");
        Assert.NotNull(regular);

        var asyncGen = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "asyncGen");
        Assert.NotNull(asyncGen);
    }

    [Fact]
    public void Extract_JavaScript_DetectsObjectLiteralMethodShorthand()
    {
        var content = """
            const obj = {
                get foo() { return 1; },
                set foo(v) { },
                *bar() { yield 1; },
                async baz() { return 1; },
                qux() { return 1; },
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        var getFoo = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "foo" && s.Line == 2);
        Assert.NotNull(getFoo);
        Assert.Equal("object", getFoo.ContainerKind);
        Assert.Equal("obj", getFoo.ContainerName);

        var setFoo = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "foo" && s.Line == 3);
        Assert.NotNull(setFoo);
        Assert.Equal("object", setFoo.ContainerKind);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "bar" && s.ContainerKind == "object");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "baz" && s.ContainerKind == "object");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "qux" && s.ContainerKind == "object");
    }

    [Fact]
    public void Extract_TypeScript_DetectsObjectLiteralMethodShorthand()
    {
        var content = """
            const obj = {
                get foo(): number { return 1; },
                set foo(v: number) { },
                *bar(): Generator<number> { yield 1; },
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "foo" && s.ContainerKind == "object" && s.ContainerName == "obj");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "bar" && s.ContainerKind == "object" && s.ContainerName == "obj");
    }

    [Fact]
    public void Extract_JavaScript_DetectsModuleExportsObjectLiteralMembers()
    {
        var content = """
            module.exports = {
                run() { return 1; },
                *gen() { yield 1; },
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run" && s.ContainerKind == "object");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "gen" && s.ContainerKind == "object");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotEmitObjectLiteralMembersInBlockScopeOrNonExportedNamespace()
    {
        // Non-exported bindings in block scope or namespace scope should be filtered out,
        // matching the scope-filter parity already applied to other JS/TS capture paths.
        // block scope や namespace 内の非 export バインディングは、他の JS/TS 抽出経路と
        // 同じスコープフィルタに合わせて除外されること。
        var content = """
            if (Math.random() > 0.5) {
              const blockScoped = {
                run() { return 1; },
              };
            }

            namespace N {
              const hidden = {
                run() { return 1; },
              };
              export const shown = {
                ok() { return 2; },
              };
            }

            export const topLevel = {
              fn() { return 3; },
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "run");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ok" && s.ContainerKind == "object");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "fn" && s.ContainerKind == "object");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotEmitObjectLiteralMembersForPlainValues()
    {
        var content = """
            const obj = {
                key: "value",
                count: 42,
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "key");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "count");
    }

    [Fact]
    public void Extract_JavaScript_DetectsClassFieldArrowWithAsiBetweenFields()
    {
        // ASI (Automatic Semicolon Insertion) between class fields must not swallow
        // the next arrow-property header into the previous expression body.
        // クラスフィールド間の ASI により式本体の後続フィールドを取りこぼさないこと。
        var content = """
            class Foo {
                first = () => 42
                second = () => 43
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        var first = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "first");
        Assert.NotNull(first);
        Assert.Equal("class", first.ContainerKind);
        Assert.Equal("Foo", first.ContainerName);

        var second = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "second");
        Assert.NotNull(second);
        Assert.Equal("class", second.ContainerKind);
        Assert.Equal("Foo", second.ContainerName);
    }

    [Fact]
    public void Extract_TypeScript_DetectsClassFieldArrowWithAsiBetweenFields()
    {
        var content = """
            class Foo {
                first = (): number => 42
                second = (x: number): number => x + 1
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var first = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "first");
        Assert.NotNull(first);
        Assert.Equal("class", first.ContainerKind);
        Assert.Equal("number", first.ReturnType);

        var second = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "second");
        Assert.NotNull(second);
        Assert.Equal("class", second.ContainerKind);
        Assert.Equal("number", second.ReturnType);
    }

    [Fact]
    public void Extract_TypeScript_DetectsClassFieldArrowAsiBeforeClosingBrace()
    {
        // Single field without trailing `;` followed by the class-closing `}` must
        // still be captured; ASI at `}` terminates the expression body.
        // セミコロンなしの単一 field が直後の class 終了 `}` で終端されるケース。
        var content = """
            class Foo {
                only = (): number => 7
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var only = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "only");
        Assert.NotNull(only);
        Assert.Equal("class", only.ContainerKind);
        Assert.Equal("Foo", only.ContainerName);
        Assert.Equal("number", only.ReturnType);
    }

    [Fact]
    public void Extract_TypeScript_DetectsMultiLineObjectLiteralBinding()
    {
        // The `{` may sit on a line after the `=` binding; collector must thread
        // the lex state across lines to find the open brace.
        // `{` が `=` バインディングと別行にあっても、collector は lex 状態を
        // 跨いで open brace を検出できること。
        var content = """
            const obj =
            {
                foo() { return 1; },
                *bar() { yield 1; },
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "foo" && s.ContainerKind == "object" && s.ContainerName == "obj");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "bar" && s.ContainerKind == "object" && s.ContainerName == "obj");
    }

    [Fact]
    public void Extract_TypeScript_DetectsExportDefaultObjectLiteralMembers()
    {
        // `export default { ... }` is a common module-shape; its shorthand members
        // should be captured with container_name == "default".
        // `export default { ... }` のショートハンドメンバは container_name == "default"
        // として抽出されること。
        var content = """
            export default {
                foo() { return 1; },
                async bar() { return 2; },
            };
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var foo = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "foo");
        Assert.NotNull(foo);
        Assert.Equal("object", foo.ContainerKind);
        Assert.Equal("default", foo.ContainerName);

        var bar = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "bar");
        Assert.NotNull(bar);
        Assert.Equal("object", bar.ContainerKind);
        Assert.Equal("default", bar.ContainerName);
    }

    [Fact]
    public void Extract_JavaScript_DetectsClassFieldArrowComputedMemberContinuation()
    {
        // A bare `[` on the next line is `foo[bar]` member-access continuation per JS ASI rules,
        // NOT a new computed class method. The scanner must not cut the expression body at `foo`.
        // JS の ASI 規則では、次行頭の `[` は `foo[bar]` メンバアクセスの継続であり、
        // computed method 名の開始ではない。式本体を `foo` で打ち切ってはならない。
        var content = """
            class Foo {
              first = () => foo
                [bar];
              second = () => 43;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        var first = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "first");
        Assert.NotNull(first);
        Assert.Equal("class", first.ContainerKind);
        Assert.Equal("Foo", first.ContainerName);
        Assert.Contains("[bar]", first.Signature);

        var second = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "second");
        Assert.NotNull(second);
        Assert.Equal("class", second.ContainerKind);
    }

    [Fact]
    public void Extract_TypeScript_DetectsClassFieldArrowComputedMemberContinuation()
    {
        var content = """
            class Foo {
              first = (): unknown => foo
                [bar];
              second = (): number => 43;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var first = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "first");
        Assert.NotNull(first);
        Assert.Equal("class", first.ContainerKind);
        Assert.Contains("[bar]", first.Signature);

        var second = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "second");
        Assert.NotNull(second);
        Assert.Equal("number", second.ReturnType);
    }

    [Fact]
    public void Extract_JavaScript_DetectsClassFieldArrowStringLiteralBeforeClosingBrace()
    {
        // A string-returning arrow without a trailing `;` must be terminated by the class-body `}`.
        // The lexer preserves opening/closing quote characters in the sanitized header, so the
        // ASI terminator check must treat `"` / `'` / `` ` `` as valid expression ends.
        // セミコロンなしで文字列を返す矢印フィールドは、直後のクラス終了 `}` で終端されなければならない。
        // lexer は開閉クォートを sanitized header 上に残すため、ASI 終端チェックは
        // `"` / `'` / `` ` `` を有効な式終端として扱わなければならない。
        var content = """
            class Foo {
              only = () => "x"
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        var only = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "only");
        Assert.NotNull(only);
        Assert.Equal("class", only.ContainerKind);
        Assert.Equal("Foo", only.ContainerName);
    }

    [Fact]
    public void Extract_JavaScript_DetectsClassFieldArrowStringLiteralWithAsiBetweenFields()
    {
        var content = """
            class Foo {
              first = () => "x"
              second = () => 43
              third = () => `template`
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first" && s.ContainerName == "Foo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second" && s.ContainerName == "Foo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "third" && s.ContainerName == "Foo");
    }

    [Fact]
    public void Extract_TypeScript_DetectsClassFieldArrowStringLiteralBeforeClosingBrace()
    {
        var content = """
            class Foo {
              only = (): string => "x"
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var only = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "only");
        Assert.NotNull(only);
        Assert.Equal("class", only.ContainerKind);
        Assert.Equal("string", only.ReturnType);
    }

    [Fact]
    public void Extract_TypeScript_DetectsClassFieldArrowStringLiteralWithAsiBetweenFields()
    {
        var content = """
            class Foo {
              first = (): string => "x"
              second = (): number => 43
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        var first = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "first");
        Assert.NotNull(first);
        Assert.Equal("string", first.ReturnType);

        var second = symbols.FirstOrDefault(s => s.Kind == "function" && s.Name == "second");
        Assert.NotNull(second);
        Assert.Equal("number", second.ReturnType);

    }

    [Fact]
    public void Extract_Csharp_LeadingBom_IndexesFirstLineImport()
    {
        // BOM-prefixed C# source: `using System;` on line 1 must still be captured.
        // Closes #183.
        // BOM 付き C# ソース: 1 行目の `using System;` も取りこぼさない。Closes #183.
        const string content = "\uFEFFusing System;\n\nnamespace BomTest;\n\npublic class WithBom {\n    public void Run() { }\n}\n";

        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var bomLess = SymbolExtractor.Extract(2, "csharp", content[1..]);
        Assert.Equal(bomLess.Count, symbols.Count);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "System" && s.Line == 1);
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "BomTest");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "WithBom");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Run");
    }

    [Fact]
    public void Extract_Python_LeadingBom_IndexesFirstLineDef()
    {
        // BOM-prefixed Python: `def at_start():` on line 1 must still be captured.
        // Closes #183.
        // BOM 付き Python: 1 行目の `def at_start():` も取りこぼさない。Closes #183.
        const string content = "\uFEFFdef at_start():\n    pass\n";

        var symbols = SymbolExtractor.Extract(1, "python", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "at_start" && s.Line == 1);
    }

    [Fact]
    public void Extract_Csharp_MidFileBom_IndexesAffectedLine()
    {
        // Mid-file BOM (e.g. from file concatenation): the `\uFEFFnamespace MidBom;` line
        // must still yield a namespace symbol, on its real line number. Closes #183.
        // ファイル連結などで挟まった mid-file BOM: `\uFEFFnamespace MidBom;` 行も
        // 実際の行番号で namespace として拾う。Closes #183.
        const string content = "using System;\n\n\uFEFFnamespace MidBom;\n\npublic class X { }\n";

        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var ns = Assert.Single(symbols.Where(s => s.Kind == "namespace"));
        Assert.Equal("MidBom", ns.Name);
        Assert.Equal(3, ns.Line);
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "System");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "X");
    }

    [Fact]
    public void Extract_NullContent_ReturnsEmpty()
    {
        // Direct callers that pass `null` must not throw. The #183 CRLF-normalization
        // step added ahead of StripLineLeadingBom would otherwise dereference `null`
        // before the helper's IsNullOrEmpty guard could run. Closes #183.
        // direct call で `null` を渡してもスローしない。#183 で StripLineLeadingBom
        // の前段に CRLF 正規化を入れたため、helper 側 IsNullOrEmpty まで届かず
        // `null` を逆参照してしまう回帰を防ぐ。Closes #183.
        Assert.Empty(SymbolExtractor.Extract(1, "csharp", null!));
    }

    [Fact]
    public void Extract_EmptyContent_ReturnsEmpty()
    {
        // Empty content returns no symbols and does not throw. Closes #183.
        // 空入力はシンボル 0 個で、例外にならない。Closes #183.
        Assert.Empty(SymbolExtractor.Extract(1, "csharp", string.Empty));
    }

    [Fact]
    public void Extract_Csharp_CrlfLeadingBom_IndexesFirstLineImport()
    {
        // Direct-call input with CRLF line endings AND a leading BOM: the CRLF → LF
        // normalization must run before StripLineLeadingBom so the line-leading BOM
        // logic still recognizes mid-file BOMs (helper treats `\n` as the sole line
        // separator). Closes #183.
        // CRLF 改行 + 先頭 BOM の direct call: StripLineLeadingBom は `\n` を唯一の
        // 行区切りとして扱うので、CRLF → LF 正規化を helper より先に通さないと
        // mid-file 行頭 BOM を剥がし損ねる。Closes #183.
        const string content = "\uFEFFusing System;\r\n\r\n\uFEFFnamespace CrlfBom;\r\n";

        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "System" && s.Line == 1);
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "CrlfBom" && s.Line == 3);
    }

    [Fact]
    public void Extract_Csharp_BareCrLeadingBom_IndexesFirstLineImport()
    {
        // Bare-`\r` direct-call input with a leading BOM: the in-extractor
        // normalization must also rewrite `\r` → `\n`, otherwise a file
        // authored under classic-Mac-style line endings would keep mid-file
        // line-leading BOMs invisible to `StripLineLeadingBom` (which treats
        // `\n` as the sole separator). Closes #183.
        // bare `\r` 改行 + 先頭 BOM の direct call: `\r` → `\n` 正規化も必要で、
        // classic-Mac 改行のファイルに対して mid-file 行頭 BOM を剥がし損ねる
        // のを防ぐ。Closes #183.
        const string content = "\uFEFFusing System;\r\r\uFEFFnamespace BareCrBom;\r";

        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "System" && s.Line == 1);
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "BareCrBom" && s.Line == 3);
    }

    [Fact]
    public void Extract_Csharp_MixedLineEndingsLeadingBom_IndexesDeclarationsOnAllLines()
    {
        // Mixed line endings (`\r\n`, bare `\r`, bare `\n`) interleaved with
        // leading + mid-file line-leading BOMs: the in-extractor normalization
        // must reduce the whole content to a `\n`-only stream before the
        // helper runs, otherwise mid-file BOMs following `\r` or `\r\n\r`
        // boundaries would survive and `^\s*`-anchored patterns would miss
        // the next declaration. Closes #183.
        // 混在改行（`\r\n` / bare `\r` / bare `\n`）+ 先頭/中間行頭 BOM の direct call:
        // 正規化を helper より先に通し、`\r\n\r` や `\r` 直後の mid-file 行頭 BOM も
        // 剥がせるようにする。Closes #183.
        const string content = "\uFEFFusing System;\r\n\r\uFEFFnamespace MixedEnds;\n\uFEFFpublic class X { }\r\n";

        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "System" && s.Line == 1);
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "MixedEnds" && s.Line == 3);
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "X" && s.Line == 4);
    }

    [Fact]
    public void Extract_CSharp_InstallScriptFixture_CompletesWithinPracticalBudget()
    {
        // issue #447 regression: the real InstallScriptTests fixture previously drove C#
        // symbol extraction into super-linear CPU time. Use the repository's current copy so
        // the regression test keeps exercising the same realistic raw-string + heredoc shape
        // that broke self-indexing, but keep the budget generous enough for slower CI hosts.
        // issue #447 回帰: 実ファイル InstallScriptTests.cs が C# シンボル抽出を super-linear に
        // 悪化させていた。自己ホストを壊した raw-string + heredoc の実形を継続的に踏むため、
        // リポジトリ内の現行ファイルをそのまま使う。時間予算は遅い CI でも耐えるよう広めに取る。
        var path = Path.Combine(GetRepositoryRoot(), "tests", "CodeIndex.Tests", "InstallScriptTests.cs");
        var content = File.ReadAllText(path);

        var stopwatch = Stopwatch.StartNew();
        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        stopwatch.Stop();

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "InstallScriptTests");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Main_WithoutExplicitVersion_DoesNotShortCircuitBrokenZeroVersionInstall");
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"InstallScriptTests.cs extraction took {stopwatch.Elapsed.TotalSeconds:F2}s, expected < 10s.");
    }

    [Fact]
    public void Extract_Java_SameLineAnnotationsCompactConstructorsAndEnumOverrides_StayIndexed()
    {
        const string content = """
            package com.example;

            public class Same {
                @Override public String toString() { return "x"; }
                @Deprecated public int legacy() { return 0; }
            }

            public enum Op {
                ADD {
                    @Override public int apply(int a, int b) { return a + b; }
                },
                SUB {
                    @Override public int apply(int a, int b) { return a - b; }
                };
                public abstract int apply(int a, int b);
            }

            public record Range(int low, int high) {
                public Range {
                    if (low > high) throw new IllegalArgumentException();
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);

        var toString = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "toString"));
        Assert.Equal("class", toString.ContainerKind);
        Assert.Equal("Same", toString.ContainerName);

        var legacy = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "legacy"));
        Assert.Equal("class", legacy.ContainerKind);
        Assert.Equal("Same", legacy.ContainerName);

        var addApply = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "apply" && s.ContainerName == "ADD"));
        Assert.Equal("function", addApply.ContainerKind);

        var subApply = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "apply" && s.ContainerName == "SUB"));
        Assert.Equal("function", subApply.ContainerKind);

        var abstractApply = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "apply" && s.ContainerName == "Op"));
        Assert.Equal("enum", abstractApply.ContainerKind);

        var compactCtor = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Range"));
        Assert.Equal("class", compactCtor.ContainerKind);
        Assert.Equal("Range", compactCtor.ContainerName);
        Assert.NotNull(compactCtor.BodyStartLine);
        Assert.NotNull(compactCtor.BodyEndLine);
    }

    [Fact]
    public void Extract_Java_SameLineCompactConstructors_StayIndexedWithoutRecordHeaderLeak()
    {
        const string content = """
            public record R(int x) {
                @Deprecated public R { if (x < 0) throw new IllegalArgumentException(); }
            }

            public record Inline(int x) { public Inline { if (x < 0) throw new IllegalArgumentException(); } }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);

        var annotatedCtor = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "R"));
        Assert.Equal("class", annotatedCtor.ContainerKind);
        Assert.Equal("R", annotatedCtor.ContainerName);
        Assert.Equal("public R { if (x < 0) throw new IllegalArgumentException(); }", annotatedCtor.Signature);
        Assert.True(string.IsNullOrEmpty(annotatedCtor.ReturnType));
        Assert.NotNull(annotatedCtor.BodyStartLine);
        Assert.NotNull(annotatedCtor.BodyEndLine);

        var inlineCtor = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Inline"));
        Assert.Equal("class", inlineCtor.ContainerKind);
        Assert.Equal("Inline", inlineCtor.ContainerName);
        Assert.Equal("public Inline { if (x < 0) throw new IllegalArgumentException(); }", inlineCtor.Signature);
        Assert.True(string.IsNullOrEmpty(inlineCtor.ReturnType));
        Assert.NotNull(inlineCtor.BodyStartLine);
        Assert.NotNull(inlineCtor.BodyEndLine);
    }

    [Fact]
    public void Extract_Java_AllmanCompactConstructors_StayIndexed()
    {
        const string content = """
            public record Sample(int value)
            {
                public Sample
                {
                    if (value < 0) throw new IllegalArgumentException();
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);

        var compactCtor = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Sample"));
        Assert.Equal("class", compactCtor.ContainerKind);
        Assert.Equal("Sample", compactCtor.ContainerName);
        Assert.Equal("public Sample", compactCtor.Signature);
        Assert.Null(compactCtor.ReturnType);
        Assert.NotNull(compactCtor.BodyStartLine);
        Assert.NotNull(compactCtor.BodyEndLine);
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "value" && s.ContainerName == "Sample");
    }

    [Fact]
    public void Extract_Java_EnumAnonymousMemberBodyMethods_StayNestedUnderMemberBodies()
    {
        const string content = """
            public enum Mix {
                A { @Override public int f() { return 1; } int g() { return 2; } },
                B { @Override public int f() { return 3; } int h() { return 4; } };
                public abstract int f();
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "A" && s.ContainerKind == "enum" && s.ContainerName == "Mix" && s.BodyStartLine != null);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "B" && s.ContainerKind == "enum" && s.ContainerName == "Mix" && s.BodyStartLine != null);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "f" && s.ContainerKind == "function" && s.ContainerName == "A");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "g" && s.ContainerKind == "function" && s.ContainerName == "A");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "f" && s.ContainerKind == "function" && s.ContainerName == "B");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "h" && s.ContainerKind == "function" && s.ContainerName == "B");
    }

    [Fact]
    public void Extract_Java_SameLineAnnotationInterfaceMembers_DoNotLeakIntoNextDeclaration()
    {
        const string content = """
            @interface Tags { String[] value(); }
            class C {}
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);

        var valueMember = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "value"));
        Assert.Equal("class", valueMember.ContainerKind);
        Assert.Equal("Tags", valueMember.ContainerName);
        Assert.Equal(1, valueMember.EndLine);
        Assert.Null(valueMember.BodyStartLine);
        Assert.Null(valueMember.BodyEndLine);
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "C" && s.StartLine == 2 && s.EndLine == 2);
    }

    [Fact]
    public void Extract_Java_SameLineAnnotationInterfaceMembers_KeepLaterMembers()
    {
        const string content = """
            @interface Tags { String[] value(); int age(); } class C {}
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);

        var valueMember = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "value"));
        var ageMember = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "age"));
        Assert.Equal("Tags", valueMember.ContainerName);
        Assert.Equal("Tags", ageMember.ContainerName);
        Assert.Equal(1, valueMember.EndLine);
        Assert.Equal(1, ageMember.EndLine);
        Assert.Null(valueMember.BodyStartLine);
        Assert.Null(ageMember.BodyStartLine);
        Assert.Equal("String[] value();", valueMember.Signature);
        Assert.Equal("int age();", ageMember.Signature);
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "C" && s.StartLine == 1 && s.EndLine == 1);
    }

    [Fact]
    public void Extract_Java_RecordHeaderAnnotationArray_KeepsCompactConstructor()
    {
        const string content = """
            @interface Tags { String[] value(); }

            public record Sample(@Tags({"a", "b"}) int value) {
                public Sample {
                    if (value < 0) throw new IllegalArgumentException();
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);

        var compactCtor = Assert.Single(symbols.Where(s => s.Kind == "function" && s.Name == "Sample"));
        Assert.Equal("class", compactCtor.ContainerKind);
        Assert.Equal("Sample", compactCtor.ContainerName);
        Assert.NotNull(compactCtor.BodyStartLine);
        Assert.NotNull(compactCtor.BodyEndLine);
        Assert.Contains("public Sample {", compactCtor.Signature);
    }

    private static string GetRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CodeIndex.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root / リポジトリルートを特定できませんでした");
    }
}
