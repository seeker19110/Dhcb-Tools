"""Exercise the real panel helper without a browser or Autodesk host."""

import shutil
import subprocess
import unittest
from pathlib import Path


@unittest.skipIf(shutil.which("node") is None, "node is unavailable")
class PanelPreviewTests(unittest.TestCase):
    def test_panel_reuses_only_a_successful_preview_and_never_repreviews_a_write(self):
        root = Path(__file__).resolve().parents[2]
        script = r"""
const fs = require('fs'), vm = require('vm'), assert = require('assert');
const source = fs.readFileSync('tools/autocad-mcp-server/panel.html', 'utf8');
const start = source.indexOf("const BASE =");
const end = source.indexOf("function setLoading", start);
let calls = [], reply = {};
const context = {location:{protocol:'http:', origin:'http://localhost'},
  fetch:async (url, opts) => {calls.push(JSON.parse(opts.body)); return {json:async()=>reply};}};
vm.createContext(context);
vm.runInContext(source.slice(start, end), context);
(async () => {
  const write = {command:'DrawingCleanup',config:{dryRun:false}};
  assert.equal((await context.apiFetch('/execute',write)).success,false);
  assert.equal(calls.length,0);
  reply = {success:true,previewToken:'token-A',documentId:'A'};
  await context.apiFetch('/execute',{command:'DrawingCleanup',config:{dryRun:true}});
  reply = {success:true};
  await context.apiFetch('/execute',write);
  await context.apiFetch('/execute',write);
  assert.equal(calls.length,3);
  assert.equal(calls[1].previewToken,'token-A');
  assert.equal(calls[2].previewToken,'token-A');
  assert.equal(calls[1].documentId,'A');
  await context.apiFetch('/execute',{...write,previewToken:'explicit',documentId:'B'});
  assert.equal(calls[3].previewToken,'explicit');
  reply = {success:false};
  await context.apiFetch('/execute',{command:'DrawingCleanup',config:{dryRun:true}});
  const count = calls.length;
  assert.equal((await context.apiFetch('/execute',write)).success,false);
  assert.equal(calls.length,count);
})().catch(error => {console.error(error);process.exitCode=1;});
"""
        result = subprocess.run(["node", "-e", script], cwd=root, capture_output=True, text=True, timeout=15)
        self.assertEqual(0, result.returncode, result.stderr)
