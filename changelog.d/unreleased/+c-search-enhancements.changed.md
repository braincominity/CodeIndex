---
category: changed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C search now indexes spaced include directives** — `# include <header.h>` forms now produce import symbols, so symbol/search navigation sees headers written with preprocessor whitespace.
- **C search now indexes `#include_next` targets** — GNU-style next-header directives now produce import symbols alongside ordinary `#include` rows.
- **C search now indexes `#import` headers** — import-style header directives now surface as import symbols for header navigation.
- **C search now indexes union declarations** — named `union` definitions now produce `union` symbols alongside structs and enums.
- **C search now indexes union typedef aliases** — forward `typedef union Name Alias;` declarations now provide searchable `union` symbols for alias names.
- **C search now indexes bracket-attributed functions** — C23-style `[[nodiscard]] int f(...)` declarations now surface by function name.
- **C references now capture `#include_next` headers** — next-header directives now appear in reference-oriented queries, not only symbol search.
- **C references now capture macro include targets** — `#include PROJECT_HEADER` now produces a header reference for macro-based include wiring.
- **C references now suppress type-keyword noise** — `struct`, `enum`, `union`, and qualifiers are filtered out of C type-reference rows so typedef edges point at real tag names.
- **C references now capture `_t` cast types** — lowercase typedef casts such as `(widget_t *)raw` now produce type-reference rows.
- **C references now capture `_t` `sizeof` operands** — `sizeof(widget_t)` now produces a type-reference row for typedef-based size checks.
- **C references now capture `_t` alignment operands** — `_Alignof(widget_t)` and `alignof(config_t)` now surface typedef operands as type references.
- **C references now capture `_t` declaration types** — local declarations such as `widget_t *current;` now point search results back to typedef names.
- **C references now capture tagged declaration types** — declarations such as `struct node *next;` now produce type references for the tag name.
- **C references now capture `_t` return types** — functions returning typedefs such as `widget_t *make_widget(void)` now reference the typedef name.
- **C references now capture tagged return types** — functions returning `struct node *` now produce type references for the returned tag.
- **C references now capture `_t` parameter types** — function parameters such as `widget_t *widget` now point back to typedef names.
- **C references now capture tagged parameter types** — parameters such as `struct node *node` now produce type references for tag names.
- **C references now capture `_t` compound literals** — literals such as `(widget_t){0}` now reference the typedef type.
- **C references now capture tagged compound literals** — literals such as `(struct node){0}` now reference the tag name.
- **C references now capture `_t` `typeof` operands** — `typeof(widget_t)` and `__typeof__(message_t *)` now reference typedef names.
- **C references now capture tagged `typeof` operands** — `typeof(struct node *)` now references the tag name.
- **C references now capture `_t` `_Generic` associations** — `_Generic(value, widget_t: ...)` now references typedef association types.
- **C references now capture tagged `_Generic` associations** — `_Generic(value, struct node *: ...)` now references tag association types.
- **C references now capture `_t` `_Atomic` type specifiers** — `_Atomic(widget_t)` now references the typedef type.

## 日本語

- **C 検索が空白付き include ディレクティブをインデックスするようになりました** — `# include <header.h>` 形式でも import シンボルを生成し、preprocessor 空白を使ったヘッダーも symbol/search navigation で見つかるようになりました。
- **C 検索が `#include_next` の参照先をインデックスするようになりました** — GNU 風の next-header ディレクティブも通常の `#include` と同じように import シンボルを生成します。
- **C 検索が `#import` ヘッダーをインデックスするようになりました** — import 形式のヘッダーディレクティブも import シンボルとして表面化し、ヘッダー移動に使えるようになりました。
- **C 検索が union 宣言をインデックスするようになりました** — 名前付き `union` 定義も struct / enum と同じように `union` シンボルを生成します。
- **C 検索が union typedef エイリアスをインデックスするようになりました** — forward `typedef union Name Alias;` 宣言でも alias 名の検索可能な `union` シンボルを生成します。
- **C 検索が角括弧属性付き関数をインデックスするようになりました** — C23 形式の `[[nodiscard]] int f(...)` 宣言も関数名で表面化します。
- **C の参照抽出が `#include_next` ヘッダーを捕捉するようになりました** — next-header ディレクティブが symbol search だけでなく参照系クエリにも出るようになりました。
- **C の参照抽出が macro include の参照先を捕捉するようになりました** — `#include PROJECT_HEADER` でも macro ベースの include 配線をヘッダー参照として生成します。
- **C の参照抽出が型キーワード由来のノイズを抑えるようになりました** — `struct` / `enum` / `union` や修飾子を C の type-reference 行から除外し、typedef edge が実際の tag 名を指すようにしました。
- **C の参照抽出が `_t` cast 型を捕捉するようになりました** — `(widget_t *)raw` のような lowercase typedef cast でも type-reference 行を生成します。
- **C の参照抽出が `_t` の `sizeof` operand を捕捉するようになりました** — `sizeof(widget_t)` でも typedef ベースの size check に対する type-reference 行を生成します。
- **C の参照抽出が `_t` の alignment operand を捕捉するようになりました** — `_Alignof(widget_t)` と `alignof(config_t)` でも typedef operand を type reference として表面化します。
- **C の参照抽出が `_t` 宣言型を捕捉するようになりました** — `widget_t *current;` のような local declaration から typedef 名へ search result が戻れるようになりました。
- **C の参照抽出が tag 付き宣言型を捕捉するようになりました** — `struct node *next;` のような宣言から tag 名の type reference を生成します。
- **C の参照抽出が `_t` 戻り値型を捕捉するようになりました** — `widget_t *make_widget(void)` のように typedef を返す関数から typedef 名への参照を生成します。
- **C の参照抽出が tag 付き戻り値型を捕捉するようになりました** — `struct node *` を返す関数から戻り値 tag の type reference を生成します。
- **C の参照抽出が `_t` parameter 型を捕捉するようになりました** — `widget_t *widget` のような関数 parameter から typedef 名へ戻れるようになりました。
- **C の参照抽出が tag 付き parameter 型を捕捉するようになりました** — `struct node *node` のような parameter から tag 名の type reference を生成します。
- **C の参照抽出が `_t` compound literal を捕捉するようになりました** — `(widget_t){0}` のような literal から typedef 型への参照を生成します。
- **C の参照抽出が tag 付き compound literal を捕捉するようになりました** — `(struct node){0}` のような literal から tag 名への参照を生成します。
- **C の参照抽出が `_t` の `typeof` operand を捕捉するようになりました** — `typeof(widget_t)` と `__typeof__(message_t *)` から typedef 名への参照を生成します。
- **C の参照抽出が tag 付き `typeof` operand を捕捉するようになりました** — `typeof(struct node *)` から tag 名への参照を生成します。
- **C の参照抽出が `_t` の `_Generic` association を捕捉するようになりました** — `_Generic(value, widget_t: ...)` から typedef association 型への参照を生成します。
- **C の参照抽出が tag 付き `_Generic` association を捕捉するようになりました** — `_Generic(value, struct node *: ...)` から tag association 型への参照を生成します。
- **C の参照抽出が `_t` の `_Atomic` type specifier を捕捉するようになりました** — `_Atomic(widget_t)` から typedef 型への参照を生成します。
