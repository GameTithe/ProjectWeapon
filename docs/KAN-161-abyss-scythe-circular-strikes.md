# 심연의 낫 광역 연속 참격

4등급 검증용 무기 `abyss_scythe`의 기본공격에는 `AbyssScytheCircularStrike` 하나를 연결한다. KAN-54 스케줄러가 반경 안 유효한 표적을 발견하면 공격을 발동한다. 시전 위치를 중심으로 즉시 한 번, 280ms 이후 같은 반경에서 한 번 더 판정한다. 검증용 수치는 반경 2.25월드 단위, 타격당 피해 14, 시전 쿨다운 1600ms다. [KAN-161](https://gametithe.atlassian.net/browse/KAN-161) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54) · [KAN-77](https://gametithe.atlassian.net/browse/KAN-77)

- `AbyssScythePattern.TryStart`는 발동 시 표적 목록으로 즉시 피해 배치와 지연 참격을 만든다. 지연 참격은 첫 시전 위치와 반경을 보존한다. `AbyssScytheFollowUp.TryResolve`가 예정 시각부터 처음 호출되면 최신 표적 목록을 다시 검사한다. 처리 프레임이 늦을 때는 그 프레임의 위치와 같은 시점을 피해에 기록한다.
- 각 판정에서 안정 표적 ID 하나당 피해 요청은 한 건이다. 두 판정은 서로 다른 피해 적용 ID로 같은 표적에도 정상 적용된다. 원의 경계는 포함한다. 사망한 표적, 공격 불가 표적과 범위 밖 표적은 제외한다.
- 지연 판정은 한 번 끝난다. 표적이 없어도 소비되며, 다른 런·확정된 결과·원래 만료 시각에서는 취소한다. 귀환 채널 중에는 스케줄러가 새 시전을 막으며, 이미 시전한 지연 판정은 활성 런에서 처리된다.
- 무기 종류의 저장 번호는 `Scythe = 7`이다. 기존 번호를 유지하며 `6`은 KAN-145 전투도끼 번호로 남겨둔다. [KAN-145](https://gametithe.atlassian.net/browse/KAN-145)

## 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.AutoAttack.Tests.AbyssScythePatternTests --output "$env:TEMP/KAN-161-scythe.xml" --timeout 300
```

씬 연출과 최종 VFX를 사용하지 않는 표적 좌표 검증이다. 테스트가 `Assets/ProjectWeapon/Weapons/PrototypeAbyssScythe/PrototypeAbyssScytheWeaponCatalog.asset`을 읽고 실제 스케줄러에서 공격을 발동한다. 반경 2.25 경계와 바깥, 동일 표적 여러 입력, 지연 중 진입·이탈·사망, 279ms와 280ms, 쿨다운 직전·정각, 만료·결과 확정·다른 런과 잘못된 설정을 확인한다.
