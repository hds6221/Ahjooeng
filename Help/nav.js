// Ahjooeng 도움말 왼쪽 목록: 홈 + 명령 목록. 지금 보는 명령은 펼쳐서 그 설명서의 항목을 보여 줍니다.
// 사용: <nav id="ahj-nav" data-home="../../Help/index.html" data-command="LGD"> (안의 링크 = 이 명령의 항목)
//       <script src="../../Help/commands.js"></script><script src="../../Help/nav.js"></script>
// commands.js를 찾지 못하면(개발 폴더 등) 원래 목록을 그대로 둡니다.
(function () {
  var nav = document.getElementById("ahj-nav");
  var commands = window.AHJOOENG_COMMANDS;
  if (!nav || !commands || !commands.length) return;
  var base = nav.getAttribute("data-base") || "";          // commands.js의 page 앞에 붙일 경로
  var home = nav.getAttribute("data-home") || "index.html";
  var current = nav.getAttribute("data-command");
  var sections = Array.prototype.slice.call(nav.childNodes); // 이 명령의 항목(문단 제목·링크)

  function el(tag, cls, text) { var e = document.createElement(tag); if (cls) e.className = cls; if (text) e.textContent = text; return e; }

  nav.innerHTML = "";
  var top = el("a", "ahj-home", "Ahjooeng 도움말 처음으로"); top.href = home; nav.appendChild(top);
  nav.appendChild(el("p", "", "명령"));
  commands.forEach(function (c) {
    var link = el("a", "ahj-cmd" + (c.command === current ? " on" : ""), c.title + " (" + c.command + ")");
    link.href = c.command === current ? "#" : base + c.page;
    nav.appendChild(link);
    if (c.command === current && sections.length) {
      var box = el("div", "ahj-sections");
      sections.forEach(function (n) { box.appendChild(n); });
      nav.appendChild(box);
    }
  });
})();
