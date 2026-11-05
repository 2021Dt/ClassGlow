"""从实际 Pydantic/OpenAPI 模型生成请求类型和三端协议常量。"""

import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def identifier(value):
    return "Api" + re.sub(r"\W", "_", value)


def typescript(schema):
    if "$ref" in schema:
        return identifier(schema["$ref"].split("/")[-1])
    if "const" in schema:
        return json.dumps(schema["const"], ensure_ascii=False)
    if "enum" in schema:
        return " | ".join(json.dumps(value, ensure_ascii=False) for value in schema["enum"])
    for keyword, separator in (("anyOf", " | "), ("oneOf", " | "), ("allOf", " & ")):
        if keyword in schema:
            return "(" + separator.join(typescript(value) for value in schema[keyword]) + ")"
    kind = schema.get("type")
    if kind == "array":
        return f"Array<{typescript(schema.get('items', {}))}>"
    if kind == "object" or "properties" in schema:
        required = set(schema.get("required", []))
        fields = [
            f"{json.dumps(key)}{'' if key in required else '?'}: {typescript(value)};"
            for key, value in schema.get("properties", {}).items()
        ]
        extra = schema.get("additionalProperties", False)
        if extra:
            fields.append(f"[key: string]: {typescript(extra) if isinstance(extra, dict) else 'unknown'};")
        return "{ " + " ".join(fields) + " }"
    return {"string": "string", "integer": "number", "number": "number", "boolean": "boolean", "null": "null"}.get(
        kind, "unknown"
    )


def outputs():
    from backend.app import app
    from backend.version import RELEASE

    schemas = app.openapi()["components"]["schemas"]
    source = "// 由 scripts.generate_contracts 生成；请修改后端模型后重新生成。\n"
    source += (
        "\n".join(f"export type {identifier(name)} = {typescript(schema)};" for name, schema in sorted(schemas.items()))
        + "\n"
    )
    return {
        "contracts/api.schema.json": json.dumps(schemas, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        "frontend/src/generated/api.ts": source,
        "frontend/src/generated/protocol.ts": f'// 由 scripts.generate_contracts 生成。\nexport const SCHEMA = {RELEASE["schema"]} as const;\nexport const ASSET_PROTOCOL = "{RELEASE["assetProtocol"]}";\n',
        "desktop/ClassroomBoard/Protocol.cs": f'// 由 scripts.generate_contracts 生成。\nnamespace ClassroomBoard;\npublic static class Protocol\n{{\n    public const int Schema = {RELEASE["schema"]};\n    public const string AssetVersion = "{RELEASE["assetProtocol"]}";\n}}\n',
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    changed = []
    for name, source in outputs().items():
        path = ROOT / name
        if not path.exists() or path.read_text(encoding="utf-8") != source:
            changed.append(name)
            if not args.check:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(source, encoding="utf-8", newline="\n")
    if changed and args.check:
        raise SystemExit("接口定义发生变化，请运行 uv run python -m scripts.generate_contracts：" + ", ".join(changed))
    print("接口契约检查通过。" if args.check else "接口契约已生成。")


if __name__ == "__main__":
    main()
