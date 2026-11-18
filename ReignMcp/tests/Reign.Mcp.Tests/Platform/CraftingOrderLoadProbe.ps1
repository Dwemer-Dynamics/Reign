param([string]$ClientAssembly,[string]$BannerlordPath,[string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
[IO.Directory]::CreateDirectory($EvidenceDirectory)|Out-Null
$sandbox=Join-Path $EvidenceDirectory 'sandbox/bin/Win64_Shipping_Client'
[IO.Directory]::CreateDirectory($sandbox)|Out-Null
Set-Location $sandbox
[Environment]::CurrentDirectory=$sandbox
$roots=@([IO.Path]::GetDirectoryName($ClientAssembly),(Join-Path $BannerlordPath 'bin/Win64_Shipping_Client'))
foreach($name in @('Native','SandBox','StoryMode','NavalDLC','Bannerlord.Harmony','Bannerlord.ButterLib','Bannerlord.UIExtenderEx','Bannerlord.MBOptionScreen')){$roots+=Join-Path $BannerlordPath ('Modules/'+$name+'/bin/Win64_Shipping_Client')}
$resolver=[ResolveEventHandler]{param($sender,$eventArgs)
    $file=([Reflection.AssemblyName]::new($eventArgs.Name)).Name+'.dll'
    foreach($root in $roots){$p=Join-Path $root $file;if(Test-Path $p){return [Reflection.Assembly]::LoadFrom($p)}}
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$flags=[Reflection.BindingFlags]'Static,Instance,Public,NonPublic'
$owner='com.bannerlordreign.crafting-order-load.v1'
$fixtureOwner='com.bannerlordreign.test.crafting-order-load'
$patch=$null;$ok=$false;$failure='';$checks=0
function Check([bool]$condition,[string]$message){if(!$condition){throw $message};$script:checks++}
function Raw([Type]$t){return [Runtime.Serialization.FormatterServices]::GetUninitializedObject($t)}
function Set-Field($obj,[string]$name,$value){$f=[HarmonyLib.AccessTools]::Field($obj.GetType(),$name);if(!$f){throw ('Missing fixture field '+$name)};$f.SetValue($obj,$value)}
function Set-Property($obj,[string]$name,$value){$p=[HarmonyLib.AccessTools]::Property($obj.GetType(),$name);$p.GetSetMethod($true).Invoke($obj,[object[]](,$value))|Out-Null}
try {
    [Reflection.Assembly]::LoadFrom((Join-Path $BannerlordPath 'Modules/Bannerlord.Harmony/bin/Win64_Shipping_Client/0Harmony.dll'))|Out-Null
    $core=[Reflection.Assembly]::LoadFrom((Join-Path $BannerlordPath 'bin/Win64_Shipping_Client/TaleWorlds.Core.dll'))
    $campaign=[Reflection.Assembly]::LoadFrom((Join-Path $BannerlordPath 'bin/Win64_Shipping_Client/TaleWorlds.CampaignSystem.dll'))
    $client=[Reflection.Assembly]::LoadFrom($ClientAssembly)
    $orderType=$campaign.GetType('TaleWorlds.CampaignSystem.CraftingSystem.CraftingOrder',$true)
    $behaviorType=$campaign.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.CraftingCampaignBehavior',$true)
    $pieceType=$core.GetType('TaleWorlds.Core.CraftingPiece',$true)
    $templateType=$core.GetType('TaleWorlds.Core.CraftingTemplate',$true)
    $designType=$core.GetType('TaleWorlds.Core.WeaponDesign',$true)
    $elementType=$core.GetType('TaleWorlds.Core.WeaponDesignElement',$true)
    $dataField=[HarmonyLib.AccessTools]::Field($orderType,'_preCraftedWeaponDesignItemData')
    $patch=$client.GetType('ReignBeta.Platform.Compatibility.ReignCraftingOrderLoadPatches',$true)
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
public static class CraftingLoadFixture {
    public static List<object> Initialized=new List<object>();
    public static int InventoryInitializations;
    public static bool Throw;
    public static bool Initialize(object __instance) {
        if(Throw) throw new InvalidOperationException("fixture unrelated initialization failure");
        Initialized.Add(__instance); return false;
    }
    public static bool Inventory() { InventoryInitializations++; return false; }
    public static void Sentinel() {}
}
'@
    $fixture=[HarmonyLib.Harmony]::new($fixtureOwner)
    $initialize=$orderType.GetMethod('InitializeCraftingOrderOnLoad')
    $validator=$orderType.GetMethod('IsPreCraftedWeaponDesignValid')
    $fixture.Patch($initialize,[HarmonyLib.HarmonyMethod]::new([CraftingLoadFixture].GetMethod('Initialize')),$null,$null,$null)|Out-Null
    $fixture.Patch([HarmonyLib.AccessTools]::Method($behaviorType,'InitializeCraftedItemData'),[HarmonyLib.HarmonyMethod]::new([CraftingLoadFixture].GetMethod('Inventory')),$null,$null,$null)|Out-Null
    $fixture.Patch($validator,$null,[HarmonyLib.HarmonyMethod]::new([CraftingLoadFixture].GetMethod('Sentinel')),$null,$null)|Out-Null
    function Order([string]$name,[int]$slot,[bool]$allowed,[bool]$valid=$true,[bool]$initialized=$true,[bool]$nativeValid=$true){
        $piece=Raw $pieceType
        Set-Property $piece 'StringId' $name
        Set-Property $piece 'IsValid' $valid
        Set-Property $piece 'IsInitialized' $initialized
        $element=Raw $elementType
        Set-Field $element '_craftingPiece' $piece
        $template=Raw $templateType
        Set-Property $template 'StringId' 'fixture-template'
        $list=[Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($pieceType))
        if($allowed){$list.Add($piece)}
        Set-Property $template 'Pieces' $list
        $buildType=$templateType.GetProperty('BuildOrders').PropertyType.GetElementType()
        if($nativeValid){Set-Property $template 'BuildOrders' ([Array]::CreateInstance($buildType,0))}
        $design=Raw $designType
        Set-Field $design 'Template' $template
        $elements=[Array]::CreateInstance($elementType,1);$elements.SetValue($element,0)
        Set-Field $design '_usedPieces' $elements
        $data=Raw $dataField.FieldType;Set-Field $data 'CraftedData' $design
        $order=Raw $orderType
        Set-Field $order '_preCraftedWeaponDesignItemData' $data
        Set-Field $order '_weaponDesignTemplate' $design
        Set-Field $order 'DifficultyLevel' $slot
        return $order
    }
    $valid=Order 'allowed' 0 $true
    $bad=Order 'spear_handle_5' 1 $false
    $invalidInitialized=Order 'invalid-initialized' 2 $true $false $true
    $nativeInvalid=Order 'native-invalid' 3 $true $true $true $false
    $optional=Order 'optional-empty' 5 $false $false $false
    Check ([bool]$validator.Invoke($bad,@())) 'Native validator no longer accepts the generator/template mismatch fixture.'
    $patch.GetMethod('Apply',$flags).Invoke($null,@())|Out-Null
    $patch.GetMethod('Apply',$flags).Invoke($null,@())|Out-Null
    Check ([bool]$validator.Invoke($bad,@())) 'Patch changed validation outside saved-order restoration.'
    $handler=$patch.GetMethod('LoadHandler',$flags).Invoke($null,@())
    Check (@([HarmonyLib.Harmony]::GetPatchInfo($validator).Postfixes|Where-Object owner -eq $owner).Count -eq 1) 'Validator patch missing/duplicated.'
    $slotsType=$campaign.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.CraftingCampaignBehavior+CraftingOrderSlots',$true)
    $slots=[Activator]::CreateInstance($slotsType)
    $array=$slotsType.GetField('Slots').GetValue($slots)
    $array.SetValue($valid,0);$array.SetValue($bad,1);$array.SetValue($invalidInitialized,2);$array.SetValue($nativeInvalid,3);$array.SetValue($optional,5)
    $custom=[HarmonyLib.AccessTools]::Field($slotsType,'_customOrders').GetValue($slots)
    $customValid=Order 'custom-valid' 0 $true
    $customBad=Order 'custom-bad' 0 $false
    $custom.Add($customValid);$custom.Add($customBad)
    $behavior=Raw $behaviorType
    $ordersField=[HarmonyLib.AccessTools]::Field($behaviorType,'_craftingOrders')
    $orders=[Activator]::CreateInstance($ordersField.FieldType)
    $town=Raw ($campaign.GetType('TaleWorlds.CampaignSystem.Settlements.Town',$true))
    $orders.Add($town,$slots);$ordersField.SetValue($behavior,$orders)
    $inventoryField=[HarmonyLib.AccessTools]::Field($behaviorType,'_craftedItemDictionary')
    $inventory=[Activator]::CreateInstance($inventoryField.FieldType)
    $item=Raw ($core.GetType('TaleWorlds.Core.ItemObject',$true))
    $inventory.Add($item,$dataField.GetValue($valid));$inventoryField.SetValue($behavior,$inventory)
    $historyField=[HarmonyLib.AccessTools]::Field($behaviorType,'_cratingItemsHistory')
    $historyField.SetValue($behavior,[Activator]::CreateInstance($historyField.FieldType))
    $handler.Invoke($behavior,@())|Out-Null
    Check ([object]::ReferenceEquals($array.GetValue(0),$valid)) 'Valid town order was changed.'
    Check ($null -eq $array.GetValue(1)) 'Incompatible town order was retained.'
    Check ($null -eq $array.GetValue(2)) 'Initialized-invalid optional piece order was retained.'
    Check ($null -eq $array.GetValue(3)) 'Native invalid-order cleanup changed.'
    Check ([object]::ReferenceEquals($array.GetValue(5),$optional)) 'Uninitialized optional empty piece was rejected.'
    Check ($custom.Count -eq 1 -and [object]::ReferenceEquals($custom[0],$customValid)) 'Custom-order cleanup did not preserve the valid order.'
    Check ([CraftingLoadFixture]::Initialized.Count -eq 3) 'Native initialized an incompatible order or skipped a valid one.'
    Check ([CraftingLoadFixture]::Initialized.Contains($valid) -and [CraftingLoadFixture]::Initialized.Contains($optional) -and [CraftingLoadFixture]::Initialized.Contains($customValid)) 'Native initialization changed the expected valid order identities.'
    Check ($patch.GetProperty('RejectedOrders',$flags).GetValue($null,$null) -eq 3) 'Rejected-order count differed.'
    Check ($inventory.Count -eq 1 -and $inventory.ContainsKey($item)) 'Crafted inventory dictionary changed.'
    Check ([CraftingLoadFixture]::InventoryInitializations -eq 1) 'Native inventory initialization was skipped/repeated by production.'
    $handler.Invoke($behavior,@())|Out-Null
    Check ($patch.GetProperty('RejectedOrders',$flags).GetValue($null,$null) -eq 3) 'Repeated load repeated removals.'
    [CraftingLoadFixture]::Throw=$true
    $thrown=$false
    try{$handler.Invoke($behavior,@())|Out-Null}catch{$thrown=$_.Exception.ToString().Contains('fixture unrelated initialization failure')}
    Check $thrown 'Unrelated load exception was swallowed.'
    Check ([bool]$validator.Invoke($bad,@())) 'Restoration scope leaked after an exception.'
    $patch.GetMethod('Unapply',$flags).Invoke($null,@())|Out-Null
    $info=[HarmonyLib.Harmony]::GetPatchInfo($validator)
    Check (!$info.Owners.Contains($owner) -and $info.Owners.Contains($fixtureOwner)) 'Cleanup changed another Harmony owner.'
    $ok=$true
}catch{$failure=$_.Exception.ToString()+[Environment]::NewLine+$_.ScriptStackTrace}
finally{
    if($patch){$patch.GetMethod('Unapply',$flags).Invoke($null,@())|Out-Null}
    if('HarmonyLib.Harmony' -as [type]){[HarmonyLib.Harmony]::new($fixtureOwner).UnpatchAll($fixtureOwner)}
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
    @{schema='reign-crafting-order-load-probe-v1';ok=$ok;checks=$checks;failure=$failure;isolation='Fresh process; native consumer and validator, process-local graph; only native engine item initialization replaced. No save/game/campaign/provider/listener.'}|ConvertTo-Json|Set-Content (Join-Path $EvidenceDirectory 'report.json')
}
if(!$ok){throw $failure}
