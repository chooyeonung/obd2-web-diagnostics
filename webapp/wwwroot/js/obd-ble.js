// obd-ble.js — Web Bluetooth ↔ ELM327(vLinker MC+ 등) 통신 모듈
// 프레임워크 독립적으로 작성: Blazor JS interop뿐 아니라 향후 다른 웹앱에서도 그대로 재사용 가능.
//
// 동작 요약:
//  connect(dotnetRef) → 기기 선택 다이얼로그 → GATT 연결 → notify/write 특성 자동 탐색
//  수신 알림은 dotnetRef.invokeMethodAsync('OnBleData', text) 로 전달
//  연결 해제 시 dotnetRef.invokeMethodAsync('OnBleDisconnected') 호출

// ELM327 계열 동글이 흔히 쓰는 GATT 서비스 후보 (vLinker MC+는 FFF0 계열)
const CANDIDATE_SERVICES = [0xfff0, 0xffe0, 0xffb0, 0x18f0];

let device = null;
let writeChar = null;
let notifyChar = null;
let dotnetRef = null;
let useWriteWithoutResponse = false;

const decoder = new TextDecoder();
const encoder = new TextEncoder();

export function isSupported() {
  return !!(navigator.bluetooth && navigator.bluetooth.requestDevice);
}

export async function connect(ref) {
  if (!isSupported()) {
    throw new Error("이 브라우저는 Web Bluetooth를 지원하지 않습니다. Chrome 또는 Edge를 사용하세요.");
  }
  dotnetRef = ref;

  device = await navigator.bluetooth.requestDevice({
    acceptAllDevices: true,
    optionalServices: CANDIDATE_SERVICES,
  });

  device.addEventListener("gattserverdisconnected", onDisconnected);

  const server = await device.gatt.connect();

  // 서비스/특성 자동 탐색: notify 가능한 특성과 write 가능한 특성을 찾는다
  for (const svcUuid of CANDIDATE_SERVICES) {
    let service;
    try {
      service = await server.getPrimaryService(svcUuid);
    } catch {
      continue; // 이 서비스는 없음 → 다음 후보
    }
    const chars = await service.getCharacteristics();
    for (const ch of chars) {
      const p = ch.properties;
      if (!notifyChar && (p.notify || p.indicate)) notifyChar = ch;
      if (!writeChar && (p.write || p.writeWithoutResponse)) {
        writeChar = ch;
        useWriteWithoutResponse = !p.write && p.writeWithoutResponse;
      }
    }
    if (notifyChar && writeChar) break;
  }

  if (!notifyChar || !writeChar) {
    await disconnect();
    throw new Error(
      "ELM327 통신 특성(notify/write)을 찾지 못했습니다. 동글이 BLE 버전인지 확인하세요."
    );
  }

  notifyChar.addEventListener("characteristicvaluechanged", onNotify);
  await notifyChar.startNotifications();

  return device.name || "(이름 없는 기기)";
}

function onNotify(event) {
  const text = decoder.decode(event.target.value);
  if (dotnetRef) dotnetRef.invokeMethodAsync("OnBleData", text);
}

function onDisconnected() {
  writeChar = null;
  notifyChar = null;
  if (dotnetRef) dotnetRef.invokeMethodAsync("OnBleDisconnected");
}

export async function send(text) {
  if (!writeChar) throw new Error("BLE가 연결되어 있지 않습니다.");
  const bytes = encoder.encode(text);
  // BLE 특성 쓰기는 MTU 제한이 있으므로 20바이트 단위로 분할 전송
  for (let i = 0; i < bytes.length; i += 20) {
    const chunk = bytes.slice(i, i + 20);
    if (useWriteWithoutResponse) await writeChar.writeValueWithoutResponse(chunk);
    else await writeChar.writeValue(chunk);
  }
}

export async function disconnect() {
  try {
    if (device && device.gatt.connected) device.gatt.disconnect();
  } finally {
    device = null;
    writeChar = null;
    notifyChar = null;
  }
}
