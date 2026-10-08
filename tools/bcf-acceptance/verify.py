"""Check actual DHCB BCF output with official 2.1 XSDs and the independent IfcOpenShell reader."""
import argparse
import json
import math
from pathlib import Path
import zipfile

from bcf.bcfxml import load
from lxml import etree


def verify(folder: Path, schemas: Path) -> dict:
    parser = etree.XMLParser(resolve_entities=False, no_network=True)
    validators = {kind: etree.XMLSchema(etree.parse(str(schemas / (kind + ".xsd")), parser))
                  for kind in ("version", "project", "markup", "visinfo")}
    reports = []
    for expected in json.loads((folder / "expected.json").read_text(encoding="utf-8")):
        path = folder / expected["file"]
        count = 0
        with zipfile.ZipFile(path) as package:
            extension = etree.fromstring(package.read("extensions.xsd"), parser)
            # Resolve the standard schema locally, with no network lookup or archive extraction.
            redefine = extension.find("{http://www.w3.org/2001/XMLSchema}redefine")
            assert redefine is not None and redefine.get("schemaLocation") == "markup.xsd"
            redefine.set("schemaLocation", str((schemas / "markup.xsd").resolve()))
            extended_markup = etree.XMLSchema(extension)
            for name in package.namelist():
                kind = ("version" if name == "bcf.version" else "project" if name == "project.bcfp"
                        else "markup" if name.endswith("/markup.bcf") else "visinfo" if name.endswith(".bcfv") else None)
                if kind:
                    xml = etree.fromstring(package.read(name), parser)
                    validators[kind].assertValid(xml)
                    if kind == "markup":
                        extended_markup.assertValid(xml)
                    count += 1
        with load(path) as project:
            assert project.version.version_id == "2.1"
            assert project.extensions is not None, "BCF 2.1 extension schema is not discoverable"
            assert len(project.topics) == len(expected["topics"])
            for item in expected["topics"]:
                topic = project.topics[item["guid"]]
                assert topic.topic.title == item["title"]
                assert topic.topic.labels == item["labels"]
                assert topic.topic.stage == item["stage"]
                if item["labels"]:
                    assert set(item["labels"]).issubset(project.extensions.topic_labels.topic_label)
                if item["stage"]:
                    assert item["stage"] in project.extensions.stages.stage
                if item["target"] is None:
                    assert not topic.viewpoints
                    continue
                assert len(topic.viewpoints) == 1
                view = next(iter(topic.viewpoints.values())).visualization_info
                camera = view.perspective_camera
                eye, direction, up = camera.camera_view_point, camera.camera_direction, camera.camera_up_vector
                d = (direction.x, direction.y, direction.z)
                u = (up.x, up.y, up.z)
                assert math.isclose(sum(x * x for x in d), 1, abs_tol=2e-6)
                assert math.isclose(sum(x * x for x in u), 1, abs_tol=2e-6)
                assert abs(sum(a * b for a, b in zip(d, u))) < 2e-6
                delta = tuple(t - p for t, p in zip(item["target"], (eye.x, eye.y, eye.z)))
                distance = math.sqrt(sum(x * x for x in delta))
                assert distance > 0 and math.isfinite(distance)
                assert all(abs(x / distance - y) < 2e-6 for x, y in zip(delta, d))
                actual_ids = [] if view.components is None else [c.authoring_tool_id for c in view.components.selection.component]
                assert actual_ids == item["authoringIds"]
        reports.append({"file": path.name, "topics": len(expected["topics"]), "validatedXmlFiles": count, "passed": True})
    return {"passed": True, "files": reports, "scope": "XSD + independent reader; receiving viewer/model selection still requires acceptance"}


if __name__ == "__main__":
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("fixtures", type=Path)
    cli.add_argument("schemas", type=Path)
    args = cli.parse_args()
    print(json.dumps(verify(args.fixtures, args.schemas), indent=2))
