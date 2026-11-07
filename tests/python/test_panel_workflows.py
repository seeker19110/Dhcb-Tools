"""Run the panel's actual JavaScript through preview, recovery and paginated DWG reads."""

import shutil
import subprocess
import unittest
from pathlib import Path


@unittest.skipIf(shutil.which("node") is None, "Node.js is required for panel workflow checks")
class PanelWorkflowTests(unittest.TestCase):
    def test_async_jobs_and_pagination_preserve_reviewed_drawing(self):
        script = r"""
const fs = require('fs'), vm = require('vm'), assert = require('assert');
const source = fs.readFileSync('tools/autocad-mcp-server/panel.html','utf8');
let calls = [], nextReply = {}, revision = 1, page = 0, drawing = 'A';
const elements = new Map();
const document = {getElementById(id) {
  if (!elements.has(id)) elements.set(id,{innerHTML:'',textContent:'',value:'',style:{},disabled:false});
  return elements.get(id);
}};
const context = {document, location:{protocol:'http:',origin:'http://localhost'},
  fetch:async (url, opts) => {
    const body = opts.body ? JSON.parse(opts.body) : null;
    calls.push({url,body});
    const response = typeof nextReply === 'function' ? nextReply(url,body) : nextReply;
    if (response instanceof Error) throw response;
    return {ok:true,json:async()=>response};
  }};
vm.createContext(context);
vm.runInContext(source.slice(source.indexOf('const BASE ='),source.indexOf('// ── Init')),context);
(async () => {
  nextReply = {id:'job/A',status:'running'};
  const previewRequest = {command:'DrawingCleanup',config:{dryRun:true,purgeUnused:true,auditErrors:true}};
  await context.executeTracked(previewRequest,{result:'cleanResult',badge:'cleanBadge'});
  assert.equal(calls[0].body.async,true);
  const count = calls.length;
  await context.executeTracked(previewRequest,{result:'cleanResult',badge:'cleanBadge'});
  assert.equal(calls.length,count,'an acknowledged job must not be submitted twice');
  nextReply = {status:'running',started:true,canCancel:true,elapsedMs:2000,progress:{stage:'scan',completed:5,total:10}};
  await context.refreshJob('job/A');
  assert.ok(elements.get('jobsList').innerHTML.includes('5/10 (50%)'));
  nextReply = {status:'done',result:{success:true,previewToken:'reviewed-token',documentId:'A',summary:'preview complete'}};
  await context.refreshJob('job/A');
  assert.ok(elements.get('cleanResult').innerHTML.includes('preview complete'));
  const write = {...previewRequest,config:{...previewRequest.config,dryRun:false},confirmation:'DELETE_UNUSED'};
  nextReply = {success:true};
  await context.apiFetch('/execute',write);
  assert.equal(calls.at(-1).body.previewToken,'reviewed-token');
  assert.equal(calls.at(-1).body.documentId,'A');
  assert.equal(calls.at(-1).body.confirmation,'DELETE_UNUSED');
  const beforeChange = calls.length;
  const rejected = await context.apiFetch('/execute',{...write,config:{...write.config,purgeUnused:false}});
  assert.equal(rejected.success,false);
  assert.equal(calls.length,beforeChange,'changed settings require a new preview');
  nextReply = new Error('network lost');
  const lostPreview = await context.apiFetch('/execute',previewRequest);
  assert.equal(lostPreview.success,false);
  nextReply = {success:true};
  const beforeStale = calls.length;
  assert.equal((await context.apiFetch('/execute',write)).success,false);
  assert.equal(calls.length,beforeStale,'failed preview must invalidate the previous token');
  nextReply = [];
  assert.equal((await context.apiFetch('/execute',{...write,documentId:'A',previewToken:'explicit'})).outcomeUnknown,true);
  assert.equal(context.jobOutcome({success:false,status:'done',result:{success:true}},'bad').success,false);

  elements.set('recoverJobId',{value:'external/job'});
  nextReply = {status:'running',canCancel:true};
  await context.recoverJob();
  nextReply = {status:'abandoned',error:'cancelled before start'};
  await context.cancelJob('external/job');
  assert.ok(calls.some(call=>call.url.endsWith('/cancel/external%2Fjob') && call.body));
  assert.equal(context.jobOutcome({status:'error',error:'failure'},'x').summary,'failure');
  assert.equal(context.jobOutcome({status:'done',result:null},'x').outcomeUnknown,true);

  calls = [];
  nextReply = (url,body) => {
    if (body.query === 'document_context') return {documentId:drawing,revision};
    if (body.query === 'text') return {texts:[],count:50,hasMore:page++===0,nextOffset:50};
    if (body.query === 'layers') return {layers:[{name:'L'+page,colorIndex:7}],count:1,hasMore:page++===0,nextOffset:500};
    return {};
  };
  await context.runQuery('text',{limit:50,textContains:'ống',layerContains:'MEP'});
  assert.equal(elements.get('queryNext').disabled,false);
  await context.turnQueryPage(1);
  const queries = calls.filter(call=>call.body?.query==='text');
  assert.equal(queries.length,2);
  assert.equal(queries[1].body.config.offset,50);
  assert.equal(queries[1].body.config.textContains,'ống');
  assert.equal(queries[1].body.documentId,'A');
  revision = 2;
  await context.turnQueryPage(-1);
  assert.equal(calls.filter(call=>call.body?.query==='text').length,2,'revision change must stop paging before querying');
  assert.ok(elements.get('queryResult').innerHTML.includes('Tải lại từ trang đầu'));
  assert.throws(()=>context.assertSameDrawing({documentId:'A'},{documentId:'B'}));

  page = 0;
  await context.loadLayers();
  assert.equal(elements.get('layersMore').disabled,false);
  await context.loadLayers(false);
  assert.equal(elements.get('layersMore').disabled,true);
  assert.ok(elements.get('layerCount').textContent.includes('2 layers'));
  page = 0;
  await context.loadLayers();
  drawing = 'B';
  await context.loadLayers(false);
  assert.ok(elements.get('layerBody').innerHTML.includes('Tải lại từ trang đầu'));
  assert.equal(elements.get('layerCount').textContent,'');
})().catch(error=>{console.error(error);process.exitCode=1;});
"""
        result = subprocess.run(["node", "-e", script], cwd=Path(__file__).resolve().parents[2],
                                capture_output=True, text=True, encoding="utf-8", timeout=20)
        self.assertEqual(0, result.returncode, result.stderr)
