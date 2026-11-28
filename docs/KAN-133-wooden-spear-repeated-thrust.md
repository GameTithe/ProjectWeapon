# KAN-133 나무 창 연속 찌르기

[KAN-133](https://gametithe.atlassian.net/browse/KAN-133) · [KAN-58](https://gametithe.atlassian.net/browse/KAN-58) · [KAN-60](https://gametithe.atlassian.net/browse/KAN-60)

1등급 나무 창의 스킬은 WoodenSpearRepeatedThrust.asset이다. 드래그를 놓은 위치와 방향으로 0·150·300ms에 세 번 찌른다. 사거리는 2.7월드 단위, 가로 허용 폭은 좌우 각 0.25단위, 타격당 피해는 11이다. 발동 시점부터 공용 0.8초와 무기 실물별 5.2초의 재사용 대기 시간을 센다. 값은 검증용이다.

## 실제 폐성 전투

1. 로비에서 나무 창을 장착하고 출격한다. 플레이 화면에서 스킬 버튼을 끌어 방향을 표시하고 놓으면 첫 찌르기가 시작된다. 이미 연결된 낡은 검과 철퇴 스킬도 기존 방식으로 사용한다.
2. 각 찌르기는 시작 때 고정한 위치를 기준으로 현재 살아 있고 가려지지 않은 표적을 다시 읽는다. 허용 범위 안의 가장 가까운 유효 실물 하나에 피해 요청을 보낸다. 시전자와 벽 너머 표적은 후보에서 제외한다. 범위에 적이 없으면 해당 단계의 피해 없이 넘어간다.
3. 한 단계마다 독립 피해 적용 ID를 사용한다. 지연 프레임에 둘 이상 예정되면, 한 번의 피해를 적용한 뒤 바뀐 표적 상태를 읽고 다음 단계를 계산한다. 예정 시각은 단계 경계 검사에 쓰고, 피해 소비자에게는 해당 프레임의 관측 시각을 전달한다. 진행 중에는 추가 자동 공격과 슬롯 교체를 보류한다.
4. 폐성은 같은 프레임의 사망·귀환 확정·마감을 먼저 처리한다. 종료 결과가 확정되었거나 마감 정각이 되면 남은 단계는 폐기한다. 살아 있는 같은 런의 귀환 채널에서는 이미 시작한 단계가 진행된다.

## Unity 재현

~~~powershell
unity test . --mode PlayMode --filter ProjectWeapon.WoodenSpearSkill.Tests --output "$env:TEMP/KAN-133-live-targeted.xml" --timeout 260
~~~

분리 검증은 실제 무기 에셋과 방향 드래그, 발동 실패, 적 선택, 정확한 타격과 재사용 시각, 귀환·사망·다른 런 경계를 다룬다. 실제 로비 검증은 두 무기를 장착하고, 폐성에서 터치 드래그로 나무 창을 발동한다. 기본 공격 사거리 밖의 실제 보스를 세 번 맞혀 피해를 구분한다. 늦은 프레임에도 두 후속 타격을 한 번씩 적용하며, 런 마감과 같은 시각의 타격이 적용되지 않는지 검증한다. [KAN-98](https://gametithe.atlassian.net/browse/KAN-98) · [KAN-176](https://gametithe.atlassian.net/browse/KAN-176) · [KAN-193](https://gametithe.atlassian.net/browse/KAN-193)
