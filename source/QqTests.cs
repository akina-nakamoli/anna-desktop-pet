using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using AnnaDesktopPet;
class QqTests {
 [STAThread] static int Main() {
  Action<bool,string> check = delegate(bool v,string m){if(!v)throw new Exception(m);};
  var req=QqCatalog.CreateRequest("晴天 & other"); check(!req.AllowAutoRedirect && req.RequestUri.Host=="c.y.qq.com" && req.RequestUri.Scheme=="https" && req.RequestUri.Query.Contains("%26"),"查询必须固定域并禁用重定向");
  string json="{\"code\":0,\"data\":{\"song\":{\"list\":[{\"songid\":123,\"songname\":\"晴天\",\"singer\":[{\"name\":\"周杰伦\"}],\"albumname\":\"叶惠美\",\"type\":0},{\"songid\":124,\"songname\":\"晴天\",\"singer\":[{\"name\":\"其他歌手\"}],\"albumname\":\"翻唱\",\"type\":0},{\"songid\":\"1&&bad\",\"songname\":\"恶意\",\"singer\":[{\"name\":\"未知\"}],\"albumname\":\"x\",\"type\":0}]}}}";
  List<QqTrack> tracks=QqCatalog.Parse(json); check(tracks.Count==2,"非法 ID 应被过滤");
  string played=null, query=null;
  var router=new MusicRouter(delegate(string s){}, null, new string[0], delegate(string q, CancellationToken c){query=q;return Task.FromResult(tracks);}, delegate(QqTrack t, CancellationToken c){played=t.Id; return Task.FromResult(0);});
  string answer=router.Handle("播放晴天").GetAwaiter().GetResult();
  check(query=="晴天" && played==null && answer.Contains("第一首"),"不明确版本应列出候选");
  answer=router.Handle("播放第2首").GetAwaiter().GetResult(); check(played=="124" && answer.Contains("发送播放请求"),"未投递选中候选");
  played=null; answer=router.Handle("播放周杰伦的晴天").GetAwaiter().GetResult(); check(played=="123","歌手+歌名唯一精确匹配");
  played=null; query=null; answer=router.Handle("播放周杰伦的《晴天》").GetAwaiter().GetResult(); check(played=="123" && query=="周杰伦的晴天","带书名号的歌手和歌名应匹配");
  played=null; query=null; answer=router.Handle("播放《晴天》").GetAwaiter().GetResult(); check(query=="晴天" && answer.Contains("第一首"),"单独带书名号的歌名应进入候选");
  check(router.Handle("不要放晴天").GetAwaiter().GetResult()==null,"否定表达不应点歌");
  bool rejected=false; try {QqCatalog.Dispatch(new QqTrack {Id="1&&bad",Type=0}, CancellationToken.None).GetAwaiter().GetResult();} catch(InvalidOperationException) {rejected=true;} check(rejected,"非法 ID 投递必须拒绝");
  bool failed=false; try{QqCatalog.Parse("{\"code\":1}");}catch(System.IO.InvalidDataException){failed=true;} check(failed,"服务错误不能当成功");
  query=null; answer=router.Handle("播放《晴天》，我的邮箱是 a@b.com").GetAwaiter().GetResult(); check(query==null && answer.Contains("个人信息"),"不得发送包含个人信息的文本");
  query=null; answer=router.Handle("播放晴天 我的手机号 13800138000").GetAwaiter().GetResult(); check(query==null && answer.Contains("个人信息"),"不得发送手机号");
  query=null; answer=router.Handle("播放《晴天》 然后告诉我秘密").GetAwaiter().GetResult(); check(query==null && answer.Contains("个人信息"),"不得发送额外句子");
  var deferred=new TaskCompletionSource<List<QqTrack>>(); played=null;
  var pendingRouter=new MusicRouter(delegate(string s){},null,new string[0],delegate(string q,CancellationToken c){return deferred.Task;},delegate(QqTrack t,CancellationToken c){played=t.Id; return Task.FromResult(0);});
  var pending=pendingRouter.Handle("播放周杰伦的晴天"); pendingRouter.CancelPending(); deferred.SetResult(tracks);
  check(pending.GetAwaiter().GetResult().Contains("取消") && played==null,"取消后不得投递"); pendingRouter.Dispose();
  tracks.Add(new QqTrack {Id="125",Title="你的样子",Artist="罗大佑",Album="x",Type=0}); played=null;
  router.Handle("播放罗大佑的你的样子").GetAwaiter().GetResult(); check(played=="125","不得删除歌名中‘的’");
  foreach(string selection in new[]{"第一首","1","第 1 首","１","选第一首","就第一首吧","播放第一首","1. 晴天 — 周杰伦（叶惠美）","１． 晴天 — 周杰伦(叶惠美)"}) {
   router.Handle("播放晴天").GetAwaiter().GetResult(); played=null; query=null;
   answer=router.Handle(selection).GetAwaiter().GetResult();
   check(played=="123" && query==null && answer.Contains("发送播放请求"),"选择必须直接投递候选，不重新搜索或落入GPT："+selection);
   played=null; check(router.Handle("1").GetAwaiter().GetResult()==null && played==null,"成功选择后不得重放过期候选");
  }
  router.Handle("播放晴天").GetAwaiter().GetResult(); played=null; query=null;
  answer=router.Handle("第9首").GetAwaiter().GetResult(); check(played==null && query==null && answer.Contains("只有"),"越界序号不得播放或搜索");
  answer=router.Handle("1. 完全不同的歌 — 未知（未知）").GetAwaiter().GetResult(); check(played==null && answer.Contains("不一致"),"复制不匹配的行不得只按编号播放");
  answer=router.Handle("第一首").GetAwaiter().GetResult(); check(played=="123","无效选择后仍应能选择有效候选");
  router.Handle("播放晴天").GetAwaiter().GetResult(); played=null;
  check(router.Handle("别放第一首").GetAwaiter().GetResult()==null && played==null,"否定句不得播放");
  check(router.Handle("1").GetAwaiter().GetResult()==null && played==null,"普通聊天后不得沿用过期候选");
  router.Handle("播放晴天").GetAwaiter().GetResult(); router.CancelPending(); played=null;
  check(router.Handle("1").GetAwaiter().GetResult()==null && played==null,"取消后不得沿用候选");
  check(router.Handle("播放第1首").GetAwaiter().GetResult().Contains("请先"),"显式选择没有列表时应给提示");
  foreach(string stop in new[]{"停止播放","别放了"}){
   router.Handle("播放晴天").GetAwaiter().GetResult(); played=null;
   check(router.Handle(stop).GetAwaiter().GetResult().Contains("取消"),"停止命令必须结束候选选择");
   check(router.Handle("1").GetAwaiter().GetResult()==null && played==null,"停止后不得用裸序号播放旧候选");
  }
  router.Handle("播放晴天").GetAwaiter().GetResult(); played=null; query=null;
  answer=router.Handle("播放晴天 我的手机号 13800138000").GetAwaiter().GetResult();
  check(query==null && answer.Contains("个人信息") && router.Handle("1").GetAwaiter().GetResult()==null && played==null,"被拒绝的新请求也必须清理旧候选");
  router.Handle("播放晴天").GetAwaiter().GetResult(); played=null;
  answer=router.Handle("播放本地 不存在的歌XYZ").GetAwaiter().GetResult();
  check(answer.Contains("没有") && router.Handle("1").GetAwaiter().GetResult()==null && played==null,"切到本地搜索不能沿用QQ候选");
  router.Dispose();
  var loveTracks=new List<QqTrack>{new QqTrack{Id="498054208",Title="LOVE 2000",Artist="遠野ひかる",Album="LOVE 2000",Type=0},new QqTrack{Id="2",Title="Love 2000 (真爱 2000)",Artist="hitomi",Album="Love 2000",Type=0}};
  var screenshot=new MusicRouter(delegate(string s){},null,new string[0],delegate(string q,CancellationToken c){return Task.FromResult(loveTracks);},delegate(QqTrack t,CancellationToken c){played=t.Id;return Task.FromResult(0);});
  foreach(string selection in new[]{"第一首","1. LOVE 2000 — 遠野ひかる（LOVE 2000）"}){
   played=null; screenshot.Handle("播放 LOVE 2000").GetAwaiter().GetResult();
   answer=screenshot.Handle(selection).GetAwaiter().GetResult(); check(played=="498054208" && answer.Contains("发送播放请求"),"截图场景必须投递远野ひかる版本："+selection);
  }
  screenshot.Dispose(); Console.WriteLine("PASS: QQ catalog, screenshot selections, exact copied rows, stale/invalid boundaries, cancellation and privacy"); return 0;
 }
}
