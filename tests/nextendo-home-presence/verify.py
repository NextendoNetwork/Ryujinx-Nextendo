# SPDX-License-Identifier: MIT
from pathlib import Path
import subprocess,json,os,argparse,shutil,tempfile
parser=argparse.ArgumentParser(description="Compile and test the actual presence publisher with a fake HTTP transport.")
parser.add_argument('--dotnet',default=shutil.which('dotnet'),help='Path to dotnet with an installed SDK and reference pack')
args=parser.parse_args()
if not args.dotnet: parser.error('dotnet SDK not found; pass --dotnet')
dotnet=Path(args.dotnet).resolve(); sdk=dotnet.parent
versions=list((sdk/'sdk').glob('*/Roslyn/bincore/csc.dll'))
if not versions: raise SystemExit('No SDK compiler found beside dotnet')
compiler=max(versions,key=lambda p:tuple(int(x) for x in p.parents[2].name.split('-')[0].split('.')))
major=compiler.parents[2].name.split('.')[0]
refpacks=[p for p in (sdk/'packs/Microsoft.NETCore.App.Ref').iterdir() if p.name.startswith(major+'.')]
if not refpacks: raise SystemExit('Matching .NET reference pack missing')
refpack=max(refpacks,key=lambda p:tuple(int(x) for x in p.name.split('-')[0].split('.')))
runtimes=[p for p in (sdk/'shared/Microsoft.NETCore.App').iterdir() if p.name.startswith(major+'.')]
if not runtimes: raise SystemExit('Matching .NET runtime missing')
runtime=max(runtimes,key=lambda p:tuple(int(x) for x in p.name.split('-')[0].split('.')))
temp=tempfile.TemporaryDirectory(prefix='nextendo-presence-'); out=Path(temp.name)
r=Path(__file__).resolve().parents[2]
s=(r/'src/Ryujinx.Common/Configuration/NextendoFriends.cs').read_text(encoding='utf-8')
start=s.index('        private static void DoPublish()');brace=s.index('{',start);depth=1;end=brace+1
while depth:
 depth+=(s[end]=='{')-(s[end]=='}');end+=1
method=s[start:end]
code='''using System;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
static class NextendoAccount { public static bool IsLinked=true; public static string NexToken="test-fixture-only"; }
static class NextendoGames { public static bool IsSystemProgram(string id) => id.StartsWith("0100000000001"); }
static class NextendoEndpoint { public static string BaseUrl() => "https://example.invalid"; }
sealed class FakeHttp {
 public readonly ManualResetEventSlim Done=new(false);
 public string Body="";
 public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) {
  Body=await request.Content.ReadAsStringAsync(); Done.Set();
  return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
 }
}
static class Program {
 static object _lock=new(); static long _lastPublishMs; static string _lastKey;
 static int _publishing; static byte[] _curAppField=Array.Empty<byte>(); static int _curStatus;
 static string CurrentTitleId="", CurrentAppDetail=""; static FakeHttp _http=new();
''' + method + '''
 static JsonDocument Publish(string id, string detail) {
  _lastPublishMs=0; _curStatus=2; CurrentTitleId=id; CurrentAppDetail=detail;
  _curAppField=new byte[]{1,2,3}; _http=new(); DoPublish();
  if(!_http.Done.Wait(5000) || !SpinWait.SpinUntil(()=>Volatile.Read(ref _publishing)==0,5000)) throw new Exception("publication did not complete");
  return JsonDocument.Parse(_http.Body);
 }
 static void Check(bool value) { if(!value) throw new Exception("presence assertion failed"); }
 public static void Main() {
  using(var home=Publish("0100000000001000", "stale game mode")) {
   var j=home.RootElement; Check(j.GetProperty("app_id").GetString()=="0100000000001000");
   Check(j.GetProperty("app_detail").GetString()=="HOME" && j.GetProperty("status").GetInt32()==1);
   Check(Encoding.UTF8.GetString(Convert.FromBase64String(j.GetProperty("app_field").GetString()))=="sys_description\\0HOME Menu · Ryujinx\\0");
  }
  using(var game=Publish("0100152000022000", "VS")) {
   var j=game.RootElement; Check(j.GetProperty("status").GetInt32()==2 && j.GetProperty("app_detail").GetString()=="VS");
   Check(j.GetProperty("app_id").GetString()=="0100152000022000" && j.GetProperty("app_field").GetString()=="AQID");
  }
  using(var system=Publish("0100000000001013", "anything")) {
   var j=system.RootElement; Check(j.GetProperty("status").GetInt32()==1 && j.GetProperty("app_id").GetString()=="" && j.GetProperty("app_detail").GetString()=="");
  }
  Console.WriteLine("Patched production DoPublish: HOME, game and other system program cases passed; no network requests.");
 }
}
'''
(out/'presence-contract.cs').write_text(code,encoding='utf-8')
refs=list((refpack/'ref'/('net'+major+'.0')).glob('*.dll'))
args=['-nologo','-target:exe','-nostdlib+',f'-out:"{out}/presence-contract.dll"']+[f'-reference:"{p}"' for p in refs]+[f'"{out}/presence-contract.cs"']
(out/'presence-contract.rsp').write_text('\n'.join(args),encoding='utf-8')
(out/'presence-contract.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net'+major+'.0','framework':{'name':'Microsoft.NETCore.App','version':runtime.name}}}),encoding='utf-8')
subprocess.run([str(dotnet),str(compiler),'@'+str(out/'presence-contract.rsp')],check=True)
subprocess.run([str(dotnet),str(out/'presence-contract.dll')],check=True)

temp.cleanup()
