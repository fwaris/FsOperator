namespace FlowValidator

module FlowDefs =
    let vzFlow rootUrl = 
        let steps  =
                [
                    Page rootUrl
                    Clicks [
                        { ElemRef.Default with elementId = Some "gnav20-sign-id-mobile"}
                    ]
                    Clicks [
                        { ElemRef.Default with elementId = Some "gnav20-sign-id-list-item-1-mobile"}
                    ]
                    Pause "Login"
                    Clicks [
                        { ElemRef.Default with aria_label = Some "Account menu list"}
                    ]
                    Clicks [
                        { ElemRef.Default with elementId = Some "gnav20-Account-L2-2-mobile"}                    
                    ]
                    Clicks [
                        { ElemRef.Default with elementId = Some "gnav20-Account-L3-29-mobile"}                    
                    ]
                    Clicks [
                        { ElemRef.Default with aria_label = Some "Contact and Billing"}                    
                    ]                    
                    Clicks [
                        { ElemRef.Default with path = Some """ion-item[data-track*=\"manage_addresses\"]"""}                    
                    ]
                    Clicks [
                        { ElemRef.Default with path = Some """ion-item[data-track*=\"billing_address\"]"""}                    
                    ]
                    Clicks [
                        { ElemRef.Default with path = Some """button[data-testid=\"Cancel\"]"""}                    
                    ]
                    Clicks [
                        { ElemRef.Default with aria_label = Some "Security"}                    
                    ]
                    Clicks [
                        { ElemRef.Default with path = Some """ion-item[data-track*=\"transfer_pin\"]"""}                    
                    ]
                    // Clicks [
                    //     { ElemRef.Default with aria_label = Some "Generate PIN"}                    
                    // ]
                    Pause "Auth for pin"
                    Pause "done"
                ]
        let extractions =
            [
                { Name="account number"; ElemRef ={ElemRef.Default with path=Some """//span[normalize-space()="Account number:"]/following-sibling::span[@data-cs-mask='true'][1]"""}}
                { Name="zip"; ElemRef ={ElemRef.Default with path=Some """input[data-testid=\"zipCode\"]"""}}
                { Name="pin"; ElemRef ={ ElemRef.Default with xpath = Some """//h2[contains(., \"Here\'s your Number Transfer PIN.\")]/following::h2[1]"""}}
                { Name="pin page account"; ElemRef ={ ElemRef.Default with xpath = Some """//h6[contains(.,\"For Account Number\")]/following::p[1]"""}}
            ]
                
        {
            FlowId="vz"
            Path = steps
            Extractions = extractions
        }
