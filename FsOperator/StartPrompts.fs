namespace FsOperator

module StartPrompts =

    let amazon =
        "https://amazon.com",
        """On Amazon, find me an iphone 16 pro max case that has
**built in screen protector** and is less than $50 with good rating.
*make sure the price is less than $50*
Use the search box to find products.
**Ignore any sign-in pages and continue without signing in**
I just want to search for products not purchase them yet.
"""


    let linkedIn =
        "https://www.linkedin.com",
        """
Summarize what my connections have posted today
on LinkedIn.
"""


    let twitter =
        "https://twitter.com",
        """
On twitter find out if anyone has posted about
generative AI in the recent past and
summarize the postings
"""

    let netflix =
        "https://www.netflix.com",
        """
On netflix.com search for well rate scifi movies
and give me a list
"""

    let jiraTasks =
        "https://jirasw.t-mobile.com/secure/RapidBoard.jspa?rapidView=23224&view=detail&selectedIssue=AGAP-7418&quickFilter=101511#",
        """

for each jira 'issue' find me the capability.
The capability can be found by following the 'Key'
link for each issue, which is a subtask.
Then finding the story then finding the parent feature.
The capability id is on that page. It starts with 'CAP'
** to find the Capability you will have to follow the links from subtask to story to feature. ***
"""
