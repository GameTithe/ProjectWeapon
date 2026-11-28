# 검증된 광고 완료의 단일 지급

보상형 광고 지급은 온라인 계정 권위가 처리한다. `AdRewardCompletionService.ProcessCallback`은 승인된 `unity_levelplay` 콜백 원문만 받는다. 의존성으로 받은 권위 검증기는 공급자 서명과 서명된 사용자·보상, 서비스 구성, 해당 계정이 서버에서 받은 정확한 예약과의 연계를 확인한 뒤 스코프, 계정 ID, 불투명 예약 토큰, 공급자 이벤트 ID를 반환한다. Dynamic UserID의 예약 토큰은 이 연계를 조회하는 키다. 지급 처리기는 권위 저장에서 계정과 예약의 일치를 다시 확인한다. 검증판의 가짜 광고 어댑터 완료는 앱 표시 상태에 사용한다. 지급 권위 모듈은 Unity의 Editor 전용 어셈블리에 있으며, 앱 플레이어 빌드에서 제외된다. [KAN-97](https://gametithe.atlassian.net/browse/KAN-97) · [KAN-55](https://gametithe.atlassian.net/browse/KAN-55) · [Unity LevelPlay S2S 콜백](https://docs.unity.com/en-us/grow/levelplay/platform/settings/server-to-server-callback) · [Unity 서버 권위](https://docs.unity.com/en-us/cloud-code/3.1.0/server-authority)

## 저장 계약

- 확인된 신규 완료는 기존 `WeightedWeaponDraw`로 한 번 추첨하여 `Personal` 창고 무기 한 개를 지급 자료에 담는다. 한 지급 레코드에 증빙 바인딩, 고유 무기 인스턴스와 예약 UTC 날짜가 포함된다. 승인된 레코드는 해당 계정·예약 날짜의 사용 1회이자 동일 거래의 반환 영수증이다. [KAN-69](https://gametithe.atlassian.net/browse/KAN-69) · [KAN-63](https://gametithe.atlassian.net/browse/KAN-63)
- 저장 어댑터는 `IAdRewardAuthorityStore.TryCommit`에서 하나의 리비전 비교와 내구 트랜잭션으로 소유권, 사용량과 영수증을 함께 확정한다. 예약 생성·해제도 지급 경로가 보는 권위 리비전을 갱신해야 한다. 환경별 저장 스코프 전체에서 공급자 이벤트와 소비된 예약의 중복을 막는다. 앱의 공용 저장 파일과 코인 뽑기에는 별도 권위 계약이 적용된다. 이번 저장 구현은 EditMode 파일 테스트 더블이다. [KAN-73](https://gametithe.atlassian.net/browse/KAN-73)
- 같은 검증된 계정·이벤트·예약이 이미 확정됐으면 기존 영수증을 반환한다. 기존 영수증은 최초 예약 만료 이후에도 동일하게 조회된다. 다른 계정이나 예약으로 이벤트가 재사용되면 충돌로 처리하고 기존 영수증을 노출하지 않는다. 원자 저장 충돌 후 기존 결과가 없으면 같은 콜백을 재시도한다. 실제 수신 확인은 권위 저장에 증빙 또는 결과가 내구적으로 남은 뒤에 보낸다. [Unity LevelPlay S2S 콜백](https://docs.unity.com/en-us/grow/levelplay/platform/settings/server-to-server-callback)
- 아직 지급하지 않은 예약은 권위 시간이 수락 마감 전이고 예약 상태가 완료를 수용할 때만 사용한다. 이 처리기에는 예약을 만드는 일일 한도, 실제 SDK·서명 검증기, 배포된 서비스, 보상 UI가 연결되지 않는다. [KAN-105](https://gametithe.atlassian.net/browse/KAN-105) · [KAN-107](https://gametithe.atlassian.net/browse/KAN-107)

## 재현

```powershell
unity test . --mode EditMode --output "$env:TEMP/KAN-97-editmode.xml" --timeout 300
```

- 검증된 증빙 한 건이 원래 예약일 사용 1회와 개인 창고 무기 하나를 저장한다. 자정 이후 역순으로 완료돼도 각 예약의 날짜가 적용된다.
- 공급자·계정·스코프·예약 불일치, 미검증 알림, 정각 만료와 시계 검증 실패는 지급을 거절한다. 파일 바이트가 유지되고 추첨도 호출되지 않는다.
- 파일 교체 직전 실패와 교체 후 응답 유실을 각각 주입한다. 새 서비스와 저장소 인스턴스로 재시도하면 미확정 건만 지급되며, 확정 건 열 회 조회는 같은 무기 ID·영수증을 반환한다.
- 두 처리기가 같은 리비전을 읽은 뒤 동시에 쓰기를 시도한다. 하나의 무기와 영수증이 저장되고 두 처리기는 결국 같은 결과를 반환한다. 예약 해제와 충돌하면 해제된 예약에 지급하지 않는다.

### 보상 확정 뒤 Windows 프로세스 강제 종료

Unity 6000.6.9f1 Editor가 포함한 .NET SDK로 테스트 자식 프로세스를 빌드하고 실행한다. 자식은 Unity가 컴파일한 실제 권위 처리기·추첨 어셈블리와 같은 파일 저장 테스트 더블을 사용한다.

```powershell
$env:PROJECTWEAPON_KAN97_CHILD = '1'
try {
    unity test . --mode EditMode --output "$env:TEMP/KAN-97-actual-child-editmode.xml" --timeout 240
} finally {
    Remove-Item Env:PROJECTWEAPON_KAN97_CHILD
}
```

부모 테스트는 격리된 파일을 준비한다. 자식이 검증된 테스트 증빙을 받아 파일을 내구적으로 교체하면 기록된 무기 ID를 부모에 알린다. 부모는 저장 바이트의 변경과 자식이 살아 있는 것을 확인하고 콜백 응답이 반환되기 전에 해당 프로세스를 강제 종료한다. 파일을 다시 열어 예약일 사용 1회, 개인 창고 무기 1개, 지급 영수증 1개를 검사한다. 만료 뒤 별도 처리기와 저장소를 열어 10회 재시도해도 동일 실물·영수증을 받고, 추첨 호출과 파일 변경은 0회다. 검증된 테스트 증빙과 로컬 파일 저장 어댑터의 프로세스 종료 경계를 다룬다. 실제 공급자 서명과 서버 백엔드는 별도 연동에서 검증한다. [KAN-37](https://gametithe.atlassian.net/browse/KAN-37)
