// Websocket connection
var function_is_running = false;
var PORT = 7307;

function read_number_param(name, fallback) {
    try {
        const params = new URLSearchParams(window.location.search);
        const raw = params.get(name);
        if (raw == null) return fallback;
        const n = Number(raw);
        return Number.isFinite(n) ? n : fallback;
    } catch {
        return fallback;
    }
}

function apply_params() {
    PORT = read_number_param("port", PORT);
    const gap = read_number_param("gap", null);
    if (gap != null) {
        document.documentElement.style.setProperty("--team-gap", `${Math.max(0, gap)}px`);
    }

    const font = read_number_param("font", null);
    if (font != null) {
        document.documentElement.style.setProperty("--overlay-font-size", `${Math.max(8, font)}px`);
    }
}

$(document).ready(connect_to_socket);

function connect_to_socket() {
    if (function_is_running) return;

    apply_params();

    console.log("Trying to connect...");
    function_is_running = true;
    let socket = new WebSocket(`ws://localhost:${PORT}`);
    socket.onopen = function (e) {
        console.log("CONNECTED");
    };
    socket.onmessage = function (event) {
        let data = JSON.parse(event.data);
        console.log(`New event: ${event.data}`);
        parse_message(data);
    };

    socket.onclose = function (event) {
        if (event.wasClean) console.log('CLEAN EXIT: ' + event);
        else console.log('UNCLEAN EXIT: ' + event);
        reconnect_to_socket();
    };

    socket.onerror = function (error) {
        console.log('ERROR: ' + error);
        reconnect_to_socket()
    };
}

function reconnect_to_socket() {
    console.log('Reconnecting..')
    function_is_running = false;
    setTimeout(function () {
        connect_to_socket();
    }, 500);
}

// Overlay functionality
var team_colors = [[74, 255, 2, 0.35], [3, 179, 255, 0.35], [255, 0, 0, 0.35]];
var custom_func = null;

function rgba_from_team(team) {
    try {
        const idx = Math.max(1, Number(team)) - 1;
        const row = team_colors[idx] || team_colors[0];
        const r = Number(row[0]) || 0;
        const g = Number(row[1]) || 0;
        const b = Number(row[2]) || 0;
        const a = Number(row[3]);
        const alpha = Number.isFinite(a) ? a : 0.35;
        return `rgba(${r}, ${g}, ${b}, ${alpha})`;
    } catch {
        return "rgba(0,0,0,0.35)";
    }
}

function normalize_country_codes(country) {
    if (country == null) return [];
    let c = `${country}`.trim().toLowerCase();
    if (c.length == 0) return [];
    c = c.replaceAll("_", "-");
    if (c == "uk") return ["gb"];
    if (c == "usa") return ["us"];
    if (c == "eng") return ["gb-eng", "gb"];
    if (c == "sct") return ["gb-sct", "gb"];
    if (c == "wls") return ["gb-wls", "gb"];
    if (c == "nir") return ["gb-nir", "gb"];
    if (c.startsWith("gb-")) return [c, "gb"];
    if (c.includes("-")) {
        const last = c.split("-").pop();
        if (last && last != c) return [c, last];
    }
    return [c];
}

function country_flag_img(country) {
    const codes = normalize_country_codes(country);
    if (!codes || codes.length == 0) return "";
    const src1 = `../img/countries/${codes[0]}.png`;
    if (codes.length >= 2) {
        const src2 = `../img/countries/${codes[1]}.png`;
        return `<img class="country-flag" src="${src1}" onerror="this.onerror=null;this.src='${src2}'">`;
    }
    return `<img class="country-flag" src="${src1}" onerror="this.style.display='none'">`;
}

function parse_message(data) {
    if (data.type == "color")
        team_colors = data.data;
    else if (data.type == "player_data")
        update_player_data(data.data)
}

function update_player_data(data) {
    $("#map").text(data.map);
    let team_data = { 1: "", 2: "" };
    let first_team = null;
    let second_team = null;
    for (const p of data.players) {
        if (first_team == null) first_team = p.team;
        let civFlag = `<td class="flag" rowspan="2"><img src="../img/flags/${p.civ}.webp"></td>`;
        let country = country_flag_img(p.country);
        let wins, losses;
        // Whether to add W/L or not
        if (p.wins == '') wins = ''; else wins = `${p.wins}W`;
        if (p.losses == '') losses = ''; else losses = `${p.losses}L`;
        // Create player element
        let s;
        const teamBg = rgba_from_team(p.team);
        if (p.team == first_team) {
            s = `<tr class="player">${civFlag}<td colspan="6" class="name player-name name-badge" style="background:${teamBg}">${p.name}</td></tr>
            <tr class="stats">
              <td class="rm">${p.rank}</td>
              <td class="rating stat">${p.rating}</td>
              <td class="winrate stat">${p.winrate}</td>
              <td class="wins stat">${wins}</td>
              <td class="losses stat">${losses}</td>
              <td class="country">${country}</td>
            </tr>
            <tr class="spacer"><td colspan="7"></td></tr>`;
        } else {
            s = `<tr class="player"><td colspan="6" class="name player-name name-badge" style="background:${teamBg}">${p.name}</td>${civFlag}</tr>
            <tr class="stats">
              <td class="country">${country}</td>
              <td class="losses stat">${losses}</td>
              <td class="wins stat">${wins}</td>
              <td class="winrate stat">${p.winrate}</td>
              <td class="rating stat">${p.rating}</td>
              <td class="rm">${p.rank}</td>
            </tr>
            <tr class="spacer"><td colspan="7"></td></tr>`;
        }
        if ([1, 2].includes(p.team))
            team_data[p.team] += s;
    }
    if (first_team == 1) second_team = 2; else second_team = 1;
    $("#team1").html(team_data[first_team]);
    $("#team2").html(team_data[second_team]);
    if (custom_func != null) custom_func(data)
}

