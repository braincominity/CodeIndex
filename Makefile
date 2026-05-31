CONFIGURATION ?= Release
FRAMEWORK ?= net8.0

.PHONY: build test lint format coverage mcp-smoke clean

build:
	CONFIGURATION="$(CONFIGURATION)" FRAMEWORK="$(FRAMEWORK)" ./dev.sh build

test:
	CONFIGURATION="$(CONFIGURATION)" FRAMEWORK="$(FRAMEWORK)" ./dev.sh test

lint:
	./dev.sh lint

format:
	./dev.sh format

coverage:
	CONFIGURATION="$(CONFIGURATION)" FRAMEWORK="$(FRAMEWORK)" ./dev.sh coverage

mcp-smoke:
	CONFIGURATION="$(CONFIGURATION)" ./dev.sh mcp-smoke

clean:
	CONFIGURATION="$(CONFIGURATION)" ./dev.sh clean
