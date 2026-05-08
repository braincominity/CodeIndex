---
category: changed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Go.cs
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Go generic function constraints are indexed as type references** — `func Decode[T WireMessage](...)` now surfaces `WireMessage` in reference search and inspect output.
- **Go generic type constraints are indexed as type references** — `type Cache[T EntityConstraint] ...` now surfaces `EntityConstraint` in reference search and inspect output.
- **Go method receiver types are indexed as type references** — `func (h *Handler) Serve(...)` now links `Handler` from reference search and inspect output.
- **Go interface method signatures expose parameter and return types** — interface members such as `Handle(ctx Context) Response` now link `Context` and `Response`.
- **Go multi-name value declarations expose their shared type** — declarations such as `var primary, secondary *Client` now link `Client`.
- **Go embedded field types are indexed as type references** — embedded fields such as `*BaseStore` and `audit.Logger` now show up in reference search.
- **Go builtin allocation type arguments are indexed as type references** — `make([]User, 0)` and `new(Client)` now link `User` and `Client` without turning `make` or `new` into calls.
- **Go type assertions are indexed as type references** — `value.(User)` and `value.(*Admin)` now link the asserted types while ignoring the `.(type)` sentinel.
- **Go function literal signatures expose parameter and return types** — callbacks such as `func(ctx Context) Result` now link `Context` and `Result`.
- **Go generic call type arguments are indexed as type references** — call sites such as `Decode[User]()` and `Map[model.Event, Result]()` now link their concrete type arguments.
- **Go function type declarations expose parameter and return types** — declarations such as `type Handler func(Request) Response` and `Callback func(Context) Result` now link their signature types.
- **Go channel type declarations expose element types** — directional channel declarations such as `<-chan Event` and `chan<- Command` now link `Event` and `Command`.
- **Go generic composite literals are indexed with their type arguments** — literals such as `Cache[Entry]{}` and `model.Set[Key, Value]{}` now link the instantiated type and concrete type arguments.
- **Go map composite literals expose key and value types** — literals such as `map[Key]Value{}` and `map[model.Tenant]*Entry{}` now link both sides of the map type.
- **Go parenthesized type conversions are indexed as type references** — idioms such as `(*Concrete)(nil)` and `(model.ID)(raw)` now link the converted type.
- **Go method expressions expose receiver types** — expressions such as `Handler.Serve`, `(*Worker).Run`, and `model.User.String` now link the receiver type.
- **Go generic instantiations without calls expose type arguments** — function values such as `Decode[User]` and `stream.Map[model.Event, Result]` now link their concrete type arguments.
- **Go interface type sets expose union term types** — constraint terms such as `~CustomID | External` and `model.Token | ~Alias` now link custom type-set members.
- **Go labels are indexed as navigation symbols** — labels such as `Retry:` now appear in symbol search and definition-oriented workflows.

## 日本語

- **Go の generic function 制約を型参照として索引するようになりました** — `func Decode[T WireMessage](...)` から `WireMessage` が reference search と inspect 出力に現れるようになりました。
- **Go の generic type 制約を型参照として索引するようになりました** — `type Cache[T EntityConstraint] ...` から `EntityConstraint` が reference search と inspect 出力に現れるようになりました。
- **Go method receiver の型を型参照として索引するようになりました** — `func (h *Handler) Serve(...)` から `Handler` が reference search と inspect 出力で辿れるようになりました。
- **Go interface method signature の引数型と戻り値型を参照として出すようになりました** — `Handle(ctx Context) Response` のような interface member から `Context` と `Response` を辿れるようになりました。
- **Go の複数名 value 宣言で共有される型を参照として出すようになりました** — `var primary, secondary *Client` のような宣言から `Client` を辿れるようになりました。
- **Go の embedded field 型を型参照として索引するようになりました** — `*BaseStore` や `audit.Logger` のような embedded field が reference search に現れるようになりました。
- **Go builtin allocation の型引数を型参照として索引するようになりました** — `make([]User, 0)` や `new(Client)` から `make` / `new` を call にせず `User` と `Client` を辿れるようになりました。
- **Go type assertion を型参照として索引するようになりました** — `value.(User)` や `value.(*Admin)` から assertion 対象型を辿れるようにしつつ、`.(type)` sentinel は無視します。
- **Go function literal signature の引数型と戻り値型を参照として出すようになりました** — `func(ctx Context) Result` のような callback から `Context` と `Result` を辿れるようになりました。
- **Go generic call の型引数を型参照として索引するようになりました** — `Decode[User]()` や `Map[model.Event, Result]()` のような call site から具体型引数を辿れるようになりました。
- **Go function type 宣言の引数型と戻り値型を参照として出すようになりました** — `type Handler func(Request) Response` や `Callback func(Context) Result` のような宣言から signature 内の型を辿れるようになりました。
- **Go channel type 宣言の要素型を参照として出すようになりました** — `<-chan Event` や `chan<- Command` のような方向付き channel 宣言から `Event` と `Command` を辿れるようになりました。
- **Go generic composite literal を型引数付きで索引するようになりました** — `Cache[Entry]{}` や `model.Set[Key, Value]{}` のような literal から生成型と具体型引数を辿れるようになりました。
- **Go map composite literal の key/value 型を参照として出すようになりました** — `map[Key]Value{}` や `map[model.Tenant]*Entry{}` のような literal から map 型の両側を辿れるようになりました。
- **Go の parenthesized type conversion を型参照として索引するようになりました** — `(*Concrete)(nil)` や `(model.ID)(raw)` のような idiom から変換対象型を辿れるようになりました。
- **Go method expression の receiver 型を参照として出すようになりました** — `Handler.Serve`、`(*Worker).Run`、`model.User.String` のような expression から receiver 型を辿れるようになりました。
- **Go の call しない generic instantiation でも型引数を参照として出すようになりました** — `Decode[User]` や `stream.Map[model.Event, Result]` のような関数値から具体型引数を辿れるようになりました。
- **Go interface type set の union term 型を参照として出すようになりました** — `~CustomID | External` や `model.Token | ~Alias` のような constraint term から custom type-set member を辿れるようになりました。
- **Go label を navigation symbol として索引するようになりました** — `Retry:` のような label が symbol search や definition 系 workflow に現れるようになりました。
