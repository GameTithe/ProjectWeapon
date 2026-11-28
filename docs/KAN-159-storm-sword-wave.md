# 폭풍의 검 검기와 번개

4등급 검증용 `storm_sword`는 독립 카탈로그에 등록하고, `StormSwordWave` 하나를 기본공격으로 연결한다. 표적 탐색 거리는 6.5월드 단위, 검기 속도는 초당 12월드 단위, 수명은 625ms, 쿨다운은 1450ms다. 직격 피해 13, 번개 반경 1.5월드 단위, 번개 피해 16은 검증용 설정값이다. [KAN-159](https://gametithe.atlassian.net/browse/KAN-159) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54)

- `StormSwordPattern.TryLaunch`는 스케줄러가 고른 표적을 향해 발동마다 검기 한 발을 만든다. 발동 시 방향을 보존한다. 표적 위치가 발동 위치와 완전히 겹친 경우 발동 위치의 접촉을 처리한다.
- 접촉 어댑터는 현재 런의 관측 시각과 발사 적용 ID를 사용해 `Advance`, `ContactTarget`, `ContactWorld`를 호출한다. 최초 유효 적 접촉은 검기를 종료하고 `StormSwordImpact`를 반환한다. 보고한 실제 접촉점 중심에 한 번만 원형 번개를 판정하며 같은 시각에 검기 직격도 반환한다. 직격과 번개는 서로 다른 적용 ID다. [KAN-72](https://gametithe.atlassian.net/browse/KAN-72) · [KAN-77](https://gametithe.atlassian.net/browse/KAN-77)
- 번개는 식별자가 같은 표적에게 한 번만 적용한다. 실제 접촉점에서 반경 경계까지 포함하며, 직격 대상도 범위에 있으면 번개 판정을 받는다. 이미 발사한 검기는 지형 접촉, 만료 시각, 탐험 결과 확정 또는 기한 도달 시 번개 없이 끝난다.

## 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.AutoAttack.Tests.StormSwordPatternTests --output "$env:TEMP/KAN-159-storm.xml" --timeout 300
```

테스트는 `PrototypeStormSwordWeaponCatalog.asset`을 읽어 실제 스케줄러와 투사체 수명을 실행한다. 접촉 좌표와 표적은 좌표 대역으로 전달한다. 발동과 사거리·쿨다운, 고정 조준 방향, 접촉 시각의 직격과 원형 번개, 표적 ID 중복, 지형과 수명·런 경계를 검증한다. 실제 충돌 어댑터와 최종 효과는 후속 연결 범위다.
