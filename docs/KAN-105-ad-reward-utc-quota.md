# KAN-105 광고 계정별 UTC 일일 제한

[KAN-105](https://gametithe.atlassian.net/browse/KAN-105) · [KAN-55](https://gametithe.atlassian.net/browse/KAN-55) · [KAN-97](https://gametithe.atlassian.net/browse/KAN-97) · [KAN-76](https://gametithe.atlassian.net/browse/KAN-76)

광고 시작 전 서버는 인증 컨텍스트의 PlayerID와 자체 UTC 시각을 읽는다. 광고를 준비하지 못했다면 예약을 만들지 않는다. 하루 동안 확정 사용과 유효 예약을 합해 최대 2회를 허용한다. 예약에 요청 UUID와 불투명 토큰, 생성 시각, 시청 허용 종료 시각과 증빙 수락 시각을 함께 기록한다. 읽기 결과는 당일 확정 사용, 보류 슬롯, 남은 슬롯, 현재 시청 여부 및 다음 UTC 자정을 제공한다. 설정 기본 가안은 시청 허용 5분과 증빙 수락 9일이다.

| 입력 | 결과 |
| --- | --- |
| 동일 개정에서 두 기기의 시청 시작 요청 | 같은 저장 개정 비교로 한 건만 시청 예약 확정. 같은 요청 UUID면 다른 기기는 기존 토큰 수신 |
| 1건 지급 및 1건 예약, 이후 세 번째 요청 | 해당 UTC 날짜의 세 번째 요청 거절 |
| 자정 전 광고가 아직 시청 중 | 새 날짜의 슬롯은 새로 계산하며, 같은 계정의 실제 시청은 한 건으로 유지 |
| 앱/SDK가 시청 종료 증빙 없이 사라짐 | 5분 시청 허용 만료 시 동시 시청만 해제. 원래 날짜의 슬롯과 9일 증빙 수락 유지 |
| 서버 시각이 저장된 생성 시각 직전으로 조정 | 같은 예약일 보류 슬롯과 활성 시청을 유지. 자정을 건너 되돌아가도 예약일은 그대로 유지 |
| 노출 여부가 불명확하거나 콜백 대기 | 신뢰된 호스트의 시청 종료 증빙으로 `AwaitingProof` 전환. 기존 날짜의 슬롯 유지, 새 날짜와 동시 시청 한도에는 영향 없음 |
| 서버가 미노출을 확인 | 미지급 예약만 한 번 해제. 기존 지급과 실물은 그대로 보존 |
| 자정 뒤 늦은 서명 콜백 | 예약한 날짜의 사용 횟수에 반영. 정확한 수락 기한부터 새 지급 거절 |
| 예전 저장에 이미 예약 3건이 남은 경우 | 확정 사용이 2회면 세 번째 신규 지급을 거절. 이미 저장한 동일 서명 이벤트는 기존 영수증 조회 가능 |

`IAdRewardReservationStore.TryWriteReservation`과 KAN-97의 `TryCommit`은 같은 저장 개정을 비교한다. 예약 상태, 지급 영수증, 확정 사용, 소유 실물이 한 원장에 들어간다. 경쟁에서 밀린 예약 요청은 최신 개정에서 다시 판정한다. 이미 확정한 지급과 뒤늦은 해제가 경합하면 지급을 보존한다. 이전 예약 형식은 미확정 대기 또는 해제 상태로 읽는다.

`IAdRewardPresentationEndVerifier`는 정확한 환경·계정·예약에 결부된 서버 시청 종료 증빙을 확인하는 호스트 경계다. 클라이언트 종료 알림은 슬롯 해제 근거가 되지 않는다. 최초 `Reserved` 응답만 새 시청을 허용한다. 연결 어댑터는 동일 토큰 최초 시작만 사용하고, 신뢰 가능한 시각으로 `AuthorizesPresentationAt`의 `ViewUntilUtc` 배타 경계를 적용한다. 재시도는 원래 토큰을 돌려주고 새 시청을 승인하지 않는다. 권위 서비스와 테스트 어댑터는 Editor 전용이고, 실제 서버 호스팅·LevelPlay 연결 및 동기화 UI는 별도 연동에 둔다. [KAN-107](https://gametithe.atlassian.net/browse/KAN-107)

## 로컬 재현

```powershell
$env:PROJECTWEAPON_KAN97_CHILD = '1'
$env:PROJECTWEAPON_KAN37_CHILD = '1'
try {
    unity test . --mode EditMode --output "$env:TEMP/KAN-105-editmode.xml" --timeout 300
    unity test . --mode PlayMode --output "$env:TEMP/KAN-105-playmode.xml" --timeout 300
} finally {
    Remove-Item Env:PROJECTWEAPON_KAN97_CHILD,Env:PROJECTWEAPON_KAN37_CHILD
}
```
